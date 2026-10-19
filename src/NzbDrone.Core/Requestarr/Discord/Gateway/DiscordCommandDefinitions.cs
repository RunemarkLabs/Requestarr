using System.Collections.Generic;
using Discord;

namespace Requestarr.Core.Discord.Gateway
{
    // Exactly two commands, registered on the one configured server (registering replaces
    // everything, so older commands disappear):
    //   /request {type} {query}  for everyone; type is autocompleted from the caller's access.
    //   /admin ...               Manage Server default permission, so non-admins never see it.
    // Visibility is cosmetic: Requestarr re-checks admin and channel on every use.
    public static class DiscordCommandDefinitions
    {
        public static readonly IReadOnlyList<string> AdminOnly = new[] { "admin" };

        public static IReadOnlyList<ApplicationCommandProperties> Build()
        {
            var request = new SlashCommandBuilder()
                .WithName("request")
                .WithDescription(DiscordText.Commands.RequestOne)
                .WithContextTypes(InteractionContextType.Guild)
                .AddOption(new SlashCommandOptionBuilder().WithName("type").WithDescription(DiscordText.Commands.OptionRequestType)
                    .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithAutocomplete(true))
                .AddOption(new SlashCommandOptionBuilder().WithName("query").WithDescription(DiscordText.Commands.OptionRequestQuery)
                    .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithMaxLength(100));

            var admin = new SlashCommandBuilder()
                .WithName("admin")
                .WithDescription(DiscordText.Commands.Admin)
                .WithContextTypes(InteractionContextType.Guild)
                .WithDefaultMemberPermissions(GuildPermission.ManageGuild)
                .AddOption(Group("user", DiscordText.Commands.AdminUser)
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
                        .AddOption(Level("level", DiscordText.Commands.OptionLevel, required: true))
                        .AddOption(new SlashCommandOptionBuilder().WithName("instance").WithDescription(DiscordText.Commands.OptionInstance)
                            .WithType(ApplicationCommandOptionType.String).WithRequired(false).WithMaxLength(100)))
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
                        .AddOption(new SlashCommandOptionBuilder().WithName("username").WithDescription(DiscordText.Commands.OptionUsername)
                            .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithMaxLength(100))))
                .AddOption(Sub("queue", DiscordText.Commands.Queue)
                    .AddOption(new SlashCommandOptionBuilder().WithName("user").WithDescription(DiscordText.Commands.OptionUserFilter)
                        .WithType(ApplicationCommandOptionType.User).WithRequired(false))
                    .AddOption(Type(required: false)))
                .AddOption(Sub("approve", DiscordText.Commands.Approve).AddOption(RequestId()))
                .AddOption(Sub("deny", DiscordText.Commands.Deny)
                    .AddOption(RequestId())
                    .AddOption(new SlashCommandOptionBuilder().WithName("reason").WithDescription(DiscordText.Commands.OptionReason)
                        .WithType(ApplicationCommandOptionType.String).WithRequired(false).WithMaxLength(400)))
                .AddOption(Group("mediaserver", DiscordText.Commands.AdminMediaServer)
                    .AddOption(Sub("approve", DiscordText.Commands.MediaServerApprove).AddOption(RequestId()))
                    .AddOption(Sub("deny", DiscordText.Commands.MediaServerDeny).AddOption(RequestId())));

            return new ApplicationCommandProperties[] { request.Build(), admin.Build() };
        }

        private static SlashCommandOptionBuilder Group(string name, string description) =>
            new SlashCommandOptionBuilder().WithName(name).WithDescription(description).WithType(ApplicationCommandOptionType.SubCommandGroup);

        private static SlashCommandOptionBuilder Sub(string name, string description) =>
            new SlashCommandOptionBuilder().WithName(name).WithDescription(description).WithType(ApplicationCommandOptionType.SubCommand);

        private static SlashCommandOptionBuilder Member() =>
            new SlashCommandOptionBuilder().WithName("member").WithDescription(DiscordText.Commands.OptionMember).WithType(ApplicationCommandOptionType.User).WithRequired(true);

        private static SlashCommandOptionBuilder RequestId() =>
            new SlashCommandOptionBuilder().WithName("id").WithDescription(DiscordText.Commands.OptionRequestId)
                .WithType(ApplicationCommandOptionType.Integer).WithRequired(true).WithMinValue(1);

        private static SlashCommandOptionBuilder Type(bool required = true) =>
            new SlashCommandOptionBuilder().WithName("type").WithDescription(DiscordText.Commands.OptionType)
                .WithType(ApplicationCommandOptionType.String).WithRequired(required)
                .AddChoice("Movies", "movie").AddChoice("TV", "tv").AddChoice("Music", "music");

        private static SlashCommandOptionBuilder Level(string name, string description, bool required) =>
            new SlashCommandOptionBuilder().WithName(name).WithDescription(description)
                .WithType(ApplicationCommandOptionType.String).WithRequired(required)
                .AddChoice("Off", "off").AddChoice("Needs approval", "approval").AddChoice("Auto-approve", "auto");
    }
}
