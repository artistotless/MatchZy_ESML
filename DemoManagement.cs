using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MatchZy
{
    public partial class MatchZy
    {
        public string demoPath = "MatchZy/";
        public string demoNameFormat = "{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}";
        public string demoUploadURL = "";
        public string demoUploadHeaderKey = "";
        public string demoUploadHeaderValue = "";

        public string demoUploadS3Url = "";
        public string demoUploadS3NotifyUrl = "";

        public string activeDemoFile = "";

        public bool isDemoRecording = false;
        public bool isDemoRecordingEnabled = true;

        public void StartDemoRecording()
        {
            if (!isDemoRecordingEnabled)
            {
                Log("[StartDemoRecording] Demo recording is disabled.");
                return;
            }
            if (isDemoRecording)
            {
                Log("[StartDemoRecording] Demo recording is already in progress.");
                return;
            }
            string demoFileName = FormatCvarValue(demoNameFormat.Replace(" ", "_")) + ".dem";
            try
            {
                string? directoryPath = Path.GetDirectoryName(Path.Join(Server.GameDirectory + "/csgo/", demoPath));
                if (directoryPath != null)
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                }
                string tempDemoPath = demoPath == "" ? demoFileName : demoPath + demoFileName;
                activeDemoFile = tempDemoPath;
                Log($"[StartDemoRecoding] Starting demo recording, path: {tempDemoPath}");
                Server.ExecuteCommand($"tv_record {tempDemoPath}");
                isDemoRecording = true;
            }
            catch (Exception ex)
            {
                Log($"[StartDemoRecording - FATAL] Error: {ex.Message}. Starting demo recording with path. Name: {demoFileName}");
                // This is to avoid demo loss in any case of exception
                Server.ExecuteCommand($"tv_record {demoFileName}");
                isDemoRecording = true;
            }

        }

        public void StopDemoRecording(float delay, string activeDemoFile, string liveMatchId, int currentMapNumber)
        {
            Log($"[StopDemoRecording] Going to stop demorecording in {delay}s");
            string demoPath = Path.Join(Server.GameDirectory + "/csgo/", activeDemoFile);
            (int t1score, int t2score) = GetTeamsScore();
            int roundNumber = t1score + t2score;
            AddTimer(delay, () =>
            {
                if (isDemoRecording)
                {
                    Server.ExecuteCommand($"tv_stoprecord");
                }
                isDemoRecording = false;
                AddTimer(15, () =>
                {
                    Task.Run(async () =>
                    {
                        await UploadFileAsync(demoPath, demoUploadURL, demoUploadHeaderKey, demoUploadHeaderValue, liveMatchId, currentMapNumber, roundNumber);
                        await UploadDemoToS3Async(demoPath, liveMatchId, currentMapNumber);
                    });
                });
            });
        }

        public async Task UploadDemoToS3Async(string filePath, string matchId, int mapNumber)
        {
            if (string.IsNullOrEmpty(demoUploadS3Url))
            {
                Log($"[UploadDemoToS3] S3 upload skipped: matchzy_demo_upload_s3_url is not set.");
                return;
            }

            try
            {
                if (!File.Exists(filePath))
                {
                    Log($"[UploadDemoToS3 ERROR] File not found: {filePath}");
                    return;
                }

                using var httpClient = new HttpClient();

                // Step 1: Request a presigned URL from the backend
                Log($"[UploadDemoToS3] Requesting presigned URL from {demoUploadS3Url} for matchId: {matchId} mapNumber: {mapNumber}");

                var requestBody = new { matchId = matchId.ToString(), mapNumber = mapNumber.ToString() };
                var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(matchConfig.RemoteLogHeaderKey) && !string.IsNullOrEmpty(matchConfig.RemoteLogHeaderValue))
                {
                    httpClient.DefaultRequestHeaders.Add(matchConfig.RemoteLogHeaderKey, matchConfig.RemoteLogHeaderValue);
                }

                HttpResponseMessage presignResponse = await httpClient.PostAsync(demoUploadS3Url, jsonContent);

                if (!presignResponse.IsSuccessStatusCode)
                {
                    Log($"[UploadDemoToS3 ERROR] Failed to get presigned URL. Status: {presignResponse.StatusCode} Response: {await presignResponse.Content.ReadAsStringAsync()}");
                    return;
                }

                string responseJson = await presignResponse.Content.ReadAsStringAsync();
                Log($"[UploadDemoToS3] Presigned URL response: {responseJson}");

                using var jsonDoc = JsonDocument.Parse(responseJson);
                if (!jsonDoc.RootElement.TryGetProperty("uploadUrl", out var uploadUrlElement))
                {
                    Log($"[UploadDemoToS3 ERROR] Response does not contain 'uploadUrl' field.");
                    return;
                }
                string uploadUrl = uploadUrlElement.GetString() ?? "";
                if (string.IsNullOrEmpty(uploadUrl))
                {
                    Log($"[UploadDemoToS3 ERROR] 'uploadUrl' is empty.");
                    return;
                }

                // Step 2: PUT the demo file to the presigned S3 URL
                Log($"[UploadDemoToS3] Uploading demo to S3: {uploadUrl}");

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                using var putContent = new ByteArrayContent(fileBytes);
                putContent.Headers.Add("Content-Type", "application/octet-stream");

                using var putRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
                putRequest.Content = putContent;

                using var s3Client = new HttpClient();
                HttpResponseMessage putResponse = await s3Client.SendAsync(putRequest);

                if (!putResponse.IsSuccessStatusCode)
                {
                    Log($"[UploadDemoToS3 ERROR] S3 PUT failed. Status: {putResponse.StatusCode} Response: {await putResponse.Content.ReadAsStringAsync()}");
                    return;
                }

                Log($"[UploadDemoToS3] Demo uploaded to S3 successfully for matchId: {matchId} mapNumber: {mapNumber} fileName: {Path.GetFileName(filePath)}");

                // Step 3: Notify the backend that upload is complete
                if (!string.IsNullOrEmpty(demoUploadS3NotifyUrl))
                {
                    Log($"[UploadDemoToS3] Sending upload notification to {demoUploadS3NotifyUrl}");

                    using var notifyClient = new HttpClient();
                    if (!string.IsNullOrEmpty(matchConfig.RemoteLogHeaderKey) && !string.IsNullOrEmpty(matchConfig.RemoteLogHeaderValue))
                    {
                        notifyClient.DefaultRequestHeaders.Add(matchConfig.RemoteLogHeaderKey, matchConfig.RemoteLogHeaderValue);
                    }

                    var notifyBody = new { matchId = matchId.ToString(), mapNumber = mapNumber.ToString() };
                    var notifyContent = new StringContent(JsonSerializer.Serialize(notifyBody), Encoding.UTF8, "application/json");

                    HttpResponseMessage notifyResponse = await notifyClient.PostAsync(demoUploadS3NotifyUrl, notifyContent);

                    if (notifyResponse.IsSuccessStatusCode)
                    {
                        Log($"[UploadDemoToS3] Notification sent successfully.");
                    }
                    else
                    {
                        Log($"[UploadDemoToS3 ERROR] Notification failed. Status: {notifyResponse.StatusCode} Response: {await notifyResponse.Content.ReadAsStringAsync()}");
                    }
                }
            }
            catch (Exception e)
            {
                Log($"[UploadDemoToS3 FATAL] An error occurred: {e.Message}");
            }
        }

        public int GetTvDelay()
        {
            bool tvEnable = ConVar.Find("tv_enable")!.GetPrimitiveValue<bool>();
            if (!tvEnable) return 0;

            bool tvEnable1 = ConVar.Find("tv_enable1")!.GetPrimitiveValue<bool>();
            int tvDelay = ConVar.Find("tv_delay")!.GetPrimitiveValue<int>();

            if (!tvEnable1) return tvDelay;
            int tvDelay1 = ConVar.Find("tv_delay1")!.GetPrimitiveValue<int>();

            if (tvDelay < tvDelay1) return tvDelay1;
            return tvDelay;
        }

        [ConsoleCommand("matchzy_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        public void DemoUploadHeaderKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string header = command.ArgByIndex(1).Trim();

            if (header != "") demoUploadHeaderKey = header;
        }

        [ConsoleCommand("matchzy_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        public void DemoUploadHeaderValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string headerValue = command.ArgByIndex(1).Trim();

            if (headerValue != "") demoUploadHeaderValue = headerValue;
        }
    }
}
