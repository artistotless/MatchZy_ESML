using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    public partial class MatchZy
    {
        [ConsoleCommand("matchzy_remote_log_url", "If defined, all events are sent to this URL over HTTP. If no protocol is provided")]
        public void RemoteLogURLCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string url = command.ArgByIndex(1);

            if (!IsValidUrl(url))
            {
                Log($"[RemoteLogURLCommand] Invalid URL: {url}. Please provide a valid URL!");
                return;
            }

            matchConfig.RemoteLogURL = url;
        }

        [ConsoleCommand("matchzy_remote_log_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for events")]
        public void RemoteLogHeaderKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string header = command.ArgByIndex(1).Trim();

            if (header != "") matchConfig.RemoteLogHeaderKey = header;
        }

        [ConsoleCommand("matchzy_remote_log_header_value", "If defined, the value of the custom header added to the events sent over HTTP")]
        public void RemoteLogHeaderValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string headerValue = command.ArgByIndex(1).Trim();

            if (headerValue != "") matchConfig.RemoteLogHeaderValue = headerValue;
        }

        [ConsoleCommand("matchzy_realtime_events_enabled", "If 1, realtime in-round events (round_start, player_death, bomb_planted, etc.) are sent to matchzy_remote_log_url")]
        public void RealtimeEventsEnabledCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string val = command.ArgByIndex(1).Trim();
            matchConfig.RealtimeEventsEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
