using System;
using Requestarr.Core.Discord;

namespace Requestarr.Core.Test.Discord.Harness
{
    // Every live check runs in the dev environment only: the dev Discord server and its bot (DEV_DISCORD_* in
    // docker/secrets.env) and the dev stack's test Sonarr and Radarr (docker\dev.ps1 -Stack dev). The QA server and
    // the QA stack (DISCORD_*, the default ports) are the owner's manual QA and are never touched by automated
    // testing; no live fixture reads their variables (Item4cReleaseFlowFixture checks).
    public static class LiveDev
    {
        public const string Stack = "dev";
        public const string ComposeProject = "requestarr-dev";
        public const string TokenVariable = "DEV_DISCORD_BOT_TOKEN";

        public static string Token => Environment.GetEnvironmentVariable(TokenVariable);

        public static ulong GuildId => ulong.Parse(Environment.GetEnvironmentVariable("DEV_DISCORD_GUILD_ID"));

        public static string RadarrUrl => $"http://localhost:{Environment.GetEnvironmentVariable("DEV_RADARR_TEST_PORT") ?? "37878"}";

        public static string SonarrUrl => $"http://localhost:{Environment.GetEnvironmentVariable("DEV_SONARR_TEST_PORT") ?? "38989"}";

        public static string RadarrKey => Environment.GetEnvironmentVariable("RADARR_TEST_API_KEY");

        public static string SonarrKey => Environment.GetEnvironmentVariable("SONARR_TEST_API_KEY");

        public static DiscordSettings Settings() => new()
        {
            BotToken = Token,
            ApplicationId = ulong.Parse(Environment.GetEnvironmentVariable("DEV_DISCORD_APPLICATION_ID")),
            GuildId = GuildId,
            AdminRoleId = ulong.TryParse(Environment.GetEnvironmentVariable("DEV_DISCORD_ADMIN_ROLE_ID"), out var adminRole) ? adminRole : null
        };
    }
}
