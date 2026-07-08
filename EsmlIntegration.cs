using System.Net.Http.Headers;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy
{
    /// <summary>
    /// Operational callbacks в ESML API: ready на старте плагина и heartbeat
    /// каждые 30 секунд. Env-vars задаёт DockerGameServerProvider при provision'е.
    /// </summary>
    public partial class MatchZy
    {
        private const float EsmlReadyDelaySeconds = 5.0f;
        private const float EsmlHeartbeatIntervalSeconds = 30.0f;
        private const int EsmlReadyMaxAttempts = 5;

        private static readonly HttpClient EsmlHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        private CounterStrikeSharp.API.Modules.Timers.Timer? esmlHeartbeatTimer;
        private bool esmlReadySent;
        private int esmlReadyAttempts;
        private string? esmlApiUrl;
        private string? esmlApiToken;

        private void InitializeEsmlIntegration()
        {
            esmlApiUrl = Environment.GetEnvironmentVariable("ESML_API_URL")?.Trim().TrimEnd('/');
            esmlApiToken = Environment.GetEnvironmentVariable("ESML_API_TOKEN")?.Trim();

            if (string.IsNullOrEmpty(esmlApiUrl) || string.IsNullOrEmpty(esmlApiToken))
            {
                Log("[EsmlIntegration] ESML_API_URL or ESML_API_TOKEN not set; server callbacks disabled.");
                return;
            }

            var gameServerId = Environment.GetEnvironmentVariable("ESML_GAMESERVER_ID");
            var externalServerId = Environment.GetEnvironmentVariable("ESML_EXTERNAL_SERVER_ID");
            Log(
                $"[EsmlIntegration] Enabled. API={esmlApiUrl}, " +
                $"gameServerId={gameServerId ?? "(unset)"}, externalId={externalServerId ?? "(unset)"}.");

            AddTimer(EsmlReadyDelaySeconds, () => _ = SendEsmlReadyAsync());
            esmlHeartbeatTimer = AddTimer(
                EsmlHeartbeatIntervalSeconds,
                () => _ = SendEsmlHeartbeatAsync(),
                TimerFlags.REPEAT);
        }

        private void ShutdownEsmlIntegration()
        {
            esmlHeartbeatTimer?.Kill();
            esmlHeartbeatTimer = null;
        }

        private async Task SendEsmlReadyAsync()
        {
            if (esmlReadySent)
                return;

            var ok = await PostEsmlAsync("/integration/matchzy/gameserver/ready");
            if (ok)
            {
                esmlReadySent = true;
                Log("[EsmlIntegration] Ready callback delivered.");
                return;
            }

            esmlReadyAttempts++;
            if (esmlReadyAttempts >= EsmlReadyMaxAttempts)
            {
                Log(
                    $"[EsmlIntegration] Ready callback failed after {EsmlReadyMaxAttempts} attempts; giving up.");
                return;
            }

            var backoff = Math.Min(30, (int)Math.Pow(2, esmlReadyAttempts));
            Log($"[EsmlIntegration] Ready callback failed; retry in {backoff}s.");
            AddTimer(backoff, () => _ = SendEsmlReadyAsync());
        }

        private async Task SendEsmlHeartbeatAsync()
        {
            await PostEsmlAsync("/integration/matchzy/gameserver/heartbeat");
        }

        private async Task<bool> PostEsmlAsync(string path)
        {
            if (string.IsNullOrEmpty(esmlApiUrl) || string.IsNullOrEmpty(esmlApiToken))
                return false;

            var url = esmlApiUrl + path;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", esmlApiToken);

                var response = await EsmlHttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    return true;

                var body = await response.Content.ReadAsStringAsync();
                Log(
                    $"[EsmlIntegration] POST {path} failed: {(int)response.StatusCode} {response.StatusCode}. " +
                    $"Body: {body}");
            }
            catch (Exception ex)
            {
                Log($"[EsmlIntegration] POST {path} error: {ex.Message}");
            }

            return false;
        }
    }
}
