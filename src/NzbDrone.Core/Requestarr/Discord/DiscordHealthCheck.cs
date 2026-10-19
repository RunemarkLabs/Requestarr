using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Localization;
using Requestarr.Core.Discord.Gateway;

namespace Requestarr.Core.Discord
{
    [CheckOn(typeof(DiscordStatusChangedEvent))]
    public class DiscordHealthCheck : HealthCheckBase
    {
        private readonly IDiscordClientHost _host;
        private readonly IDiscordGuildGateway _gateway;

        public DiscordHealthCheck(IDiscordClientHost host, IDiscordGuildGateway gateway, ILocalizationService localizationService)
            : base(localizationService)
        {
            _host = host;
            _gateway = gateway;
        }

        public override HealthCheck Check()
        {
            return _host.State switch
            {
                DiscordBotState.Connected when _gateway.IsConnected && !_gateway.CanPinMessages => Problem(HealthCheckResult.Warning, HealthCheckReason.DiscordMissingPermission, DiscordText.MissingPinMessages, "#discord-missing-permission"),
                DiscordBotState.Connected => new HealthCheck(GetType()),
                DiscordBotState.NotConfigured => Problem(HealthCheckResult.Warning, HealthCheckReason.DiscordNotConfigured, "The Discord bot is not configured. Add the discord section and bot token to the provisioning file.", "#discord-not-configured"),
                DiscordBotState.Connecting => Problem(HealthCheckResult.Warning, HealthCheckReason.DiscordDisconnected, "The Discord bot is connecting.", "#discord-disconnected"),
                _ => Problem(HealthCheckResult.Error, HealthCheckReason.DiscordDisconnected, $"The Discord bot is not connected{(_host.LastError != null ? ": " + _host.LastError : string.Empty)}", "#discord-disconnected")
            };
        }

        private HealthCheck Problem(HealthCheckResult result, HealthCheckReason reason, string message, string wikiFragment)
        {
            return new HealthCheck(GetType(), result, reason, message, wikiFragment);
        }
    }
}
