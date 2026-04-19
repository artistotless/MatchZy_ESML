using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        private static readonly HttpClient realtimeHttpClient = new();

        public async Task SendRealtimeEventAsync(MatchZyEvent @event)
        {
            try
            {
                if (!matchConfig.RealtimeEventsEnabled) return;
                if (string.IsNullOrEmpty(matchConfig.RemoteLogURL)) return;

                Log($"[SendRealtimeEventAsync] Sending: {@event.EventName} to {matchConfig.RemoteLogURL}");

                using var content = new StringContent(
                    JsonSerializer.Serialize(@event, @event.GetType()),
                    Encoding.UTF8,
                    "application/json"
                );

                using var request = new HttpRequestMessage(HttpMethod.Post, matchConfig.RemoteLogURL) { Content = content };

                if (!string.IsNullOrEmpty(matchConfig.RemoteLogHeaderKey) && !string.IsNullOrEmpty(matchConfig.RemoteLogHeaderValue))
                {
                    request.Headers.TryAddWithoutValidation(matchConfig.RemoteLogHeaderKey, matchConfig.RemoteLogHeaderValue);
                }

                var response = await realtimeHttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"[SendRealtimeEventAsync] {@event.EventName} failed: {response.StatusCode}");
                }
            }
            catch (Exception e)
            {
                Log($"[SendRealtimeEventAsync FATAL] An error occurred: {e.Message}");
            }
        }

        // Builds a RealtimePlayerInfo snapshot for a given controller.
        private RealtimePlayerInfo? BuildPlayerInfo(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return null;

            var pawn = player.PlayerPawn?.Value;
            string side = player.TeamNum switch
            {
                (int)CsTeam.CounterTerrorist => "CT",
                (int)CsTeam.Terrorist        => "T",
                _                             => "Spectator"
            };

            return new RealtimePlayerInfo
            {
                SteamId  = player.SteamID.ToString(),
                Name     = player.PlayerName,
                Team     = side,
                Alive    = pawn?.LifeState == (byte)LifeState_t.LIFE_ALIVE,
                Hp       = pawn?.Health ?? 0,
                Armor    = pawn?.ArmorValue ?? 0,
                Money    = player.InGameMoneyServices?.Account ?? 0,
                Kills    = player.ActionTrackingServices?.MatchStats?.Kills ?? 0,
                Deaths   = player.ActionTrackingServices?.MatchStats?.Deaths ?? 0,
                Assists  = player.ActionTrackingServices?.MatchStats?.Assists ?? 0,
            };
        }

        private List<RealtimePlayerInfo> BuildAllPlayersInfo()
        {
            var result = new List<RealtimePlayerInfo>();
            foreach (var p in Utilities.GetPlayers())
            {
                var info = BuildPlayerInfo(p);
                if (info != null) result.Add(info);
            }
            return result;
        }

        private string CurrentMapName() => Server.MapName ?? "unknown";
    }
}
