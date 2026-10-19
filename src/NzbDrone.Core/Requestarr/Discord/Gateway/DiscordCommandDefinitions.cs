using System.Collections.Generic;
using Discord;

namespace Requestarr.Core.Discord.Gateway
{
    // Static slash command definitions, registered on the one configured server. Visibility
    // is not used for security: Requestarr re-checks who may run what on every interaction.
    public static class DiscordCommandDefinitions
    {
        public static IReadOnlyList<ApplicationCommandProperties> Build()
        {
            var user = new SlashCommandBuilder()
                .WithName("user")
                .WithDescription(DiscordText.Commands.User)
                .WithContextTypes(InteractionContextType.Guild)
                .AddOption(Sub("add", DiscordText.Commands.UserAdd)
                    .AddOption(Member())
                    .AddOption(Level("movie", DiscordText.Commands.OptionMovie, required: false))
                    .AddOption(Level("tv", DiscordText.Commands.OptionTv, required: false))
                    .AddOption(Level("music", DiscordText.Commands.OptionMusic, required: false)))
                .AddOption(Sub("remove", DiscordText.Commands.UserRemove).AddOption(Member()))
                .AddOption(Sub("suspend", DiscordText.Commands.UserSuspend).AddOption(Member()))
                .AddOption(Sub("unsuspend", DiscordText.Commands.UserUnsuspend).AddOption(Member()))
                .AddOption(Sub("access", DiscordText.Commands.UserAccess)
                    .AddOption(Member())
                    .AddOption(Type())
                    .AddOption(Level("level", DiscordText.Commands.OptionLevel, required: true)))
                .AddOption(Sub("quota", DiscordText.Commands.UserQuota)
                    .AddOption(Member())
                    .AddOption(Type())
                    .AddOption(new SlashCommandOptionBuilder().WithName("limit").WithDescription(DiscordText.Commands.OptionLimit)
                        .WithType(ApplicationCommandOptionType.Integer).WithRequired(true).WithMinValue(0).WithMaxValue(1000))
                    .AddOption(new SlashCommandOptionBuilder().WithName("days").WithDescription(DiscordText.Commands.OptionDays)
                        .WithType(ApplicationCommandOptionType.Integer).WithRequired(false).WithMinValue(1).WithMaxValue(365)))
                .AddOption(Sub("info", DiscordText.Commands.UserInfo).AddOption(Member()))
                .AddOption(Sub("mediaserver", DiscordText.Commands.UserMediaServer)
                    .AddOption(Member())
                    .AddOption(new SlashCommandOptionBuilder().WithName("service").WithDescription(DiscordText.Commands.OptionService)
                        .WithType(ApplicationCommandOptionType.String).WithRequired(true).AddChoice("Plex", "plex"))
                    .AddOption(Username()));

            var mediaServer = new SlashCommandBuilder()
                .WithName("mediaserver")
                .WithDescription(DiscordText.Commands.MediaServer)
                .WithContextTypes(InteractionContextType.Guild)
                .AddOption(Sub("request", DiscordText.Commands.MediaServerRequest).AddOption(Username()));

            return new ApplicationCommandProperties[] { user.Build(), mediaServer.Build() };
        }

        private static SlashCommandOptionBuilder Sub(string name, string description) =>
            new SlashCommandOptionBuilder().WithName(name).WithDescription(description).WithType(ApplicationCommandOptionType.SubCommand);

        private static SlashCommandOptionBuilder Member() =>
            new SlashCommandOptionBuilder().WithName("member").WithDescription(DiscordText.Commands.OptionMember).WithType(ApplicationCommandOptionType.User).WithRequired(true);

        private static SlashCommandOptionBuilder Username() =>
            new SlashCommandOptionBuilder().WithName("username").WithDescription(DiscordText.Commands.OptionUsername)
                .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithMaxLength(100);

        private static SlashCommandOptionBuilder Type() =>
            new SlashCommandOptionBuilder().WithName("type").WithDescription(DiscordText.Commands.OptionType)
                .WithType(ApplicationCommandOptionType.String).WithRequired(true)
                .AddChoice("Movies", "movie").AddChoice("TV", "tv").AddChoice("Music", "music");

        private static SlashCommandOptionBuilder Level(string name, string description, bool required) =>
            new SlashCommandOptionBuilder().WithName(name).WithDescription(description)
                .WithType(ApplicationCommandOptionType.String).WithRequired(required)
                .AddChoice("Off", "off").AddChoice("Needs approval", "approval").AddChoice("Auto-approve", "auto");
    }
}
