using System.Collections.Generic;
using Discord;

namespace Requestarr.Core.Discord.Gateway
{
    // Static slash command definitions, registered on the one configured server.
    // Admin-only commands carry Discord's Manage Server default permission so ordinary members
    // don't see them; that is cosmetic. Requestarr still checks admin and channel on every use.
    // Commands users need (including ones the admin shares) stay visible to everyone.
    public static class DiscordCommandDefinitions
    {
        public static readonly IReadOnlyList<string> AdminOnly = new[] { "user", "queue", "approve", "deny" };

        public static IReadOnlyList<ApplicationCommandProperties> Build()
        {
            var user = Admin(new SlashCommandBuilder()
                .WithName("user")
                .WithDescription(DiscordText.Commands.User)
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
                    .AddOption(Username())));

            var queue = Admin(new SlashCommandBuilder()
                .WithName("queue")
                .WithDescription(DiscordText.Commands.Queue)
                .AddOption(OptionalUser())
                .AddOption(Type(required: false)));

            var approve = Admin(new SlashCommandBuilder()
                .WithName("approve")
                .WithDescription(DiscordText.Commands.Approve)
                .AddOption(RequestId()));

            var deny = Admin(new SlashCommandBuilder()
                .WithName("deny")
                .WithDescription(DiscordText.Commands.Deny)
                .AddOption(RequestId())
                .AddOption(new SlashCommandOptionBuilder().WithName("reason").WithDescription(DiscordText.Commands.OptionReason)
                    .WithType(ApplicationCommandOptionType.String).WithRequired(false).WithMaxLength(400)));

            var request = Shared(new SlashCommandBuilder()
                .WithName("request")
                .WithDescription(DiscordText.Commands.Request)
                .AddOption(Sub("movie", DiscordText.Commands.RequestMovie).AddOption(Query()))
                .AddOption(Sub("tv", DiscordText.Commands.RequestTv).AddOption(Query()))
                .AddOption(Sub("movie-tmdb", DiscordText.Commands.RequestMovieTmdb).AddOption(ExternalId("id", DiscordText.Commands.OptionTmdbId)))
                .AddOption(Sub("tv-tvdb", DiscordText.Commands.RequestTvTvdb).AddOption(ExternalId("id", DiscordText.Commands.OptionTvdbId)))
                .AddOption(Sub("cancel", DiscordText.Commands.RequestCancel).AddOption(RequestId())));

            var requests = Shared(new SlashCommandBuilder()
                .WithName("requests")
                .WithDescription(DiscordText.Commands.Requests)
                .AddOption(new SlashCommandOptionBuilder().WithName("status").WithDescription(DiscordText.Commands.OptionStatus)
                    .WithType(ApplicationCommandOptionType.String).WithRequired(false)
                    .AddChoice("Pending", "pending").AddChoice("Approved", "approved").AddChoice("Available", "available")
                    .AddChoice("Denied", "denied").AddChoice("Cancelled", "cancelled").AddChoice("Failed", "failed"))
                .AddOption(Type(required: false))
                .AddOption(OptionalUser()));

            var quota = Shared(new SlashCommandBuilder()
                .WithName("quota")
                .WithDescription(DiscordText.Commands.Quota)
                .AddOption(OptionalUser()));

            var mediaServer = Shared(new SlashCommandBuilder()
                .WithName("mediaserver")
                .WithDescription(DiscordText.Commands.MediaServer)
                .AddOption(Sub("request", DiscordText.Commands.MediaServerRequest).AddOption(Username()))
                .AddOption(Sub("approve", DiscordText.Commands.MediaServerApprove).AddOption(RequestId()))
                .AddOption(Sub("deny", DiscordText.Commands.MediaServerDeny).AddOption(RequestId())));

            return new ApplicationCommandProperties[]
            {
                user.Build(), queue.Build(), approve.Build(), deny.Build(), request.Build(), requests.Build(), quota.Build(), mediaServer.Build()
            };
        }

        private static SlashCommandBuilder Admin(SlashCommandBuilder builder) =>
            builder.WithContextTypes(InteractionContextType.Guild).WithDefaultMemberPermissions(GuildPermission.ManageGuild);

        private static SlashCommandBuilder Shared(SlashCommandBuilder builder) =>
            builder.WithContextTypes(InteractionContextType.Guild);

        private static SlashCommandOptionBuilder Sub(string name, string description) =>
            new SlashCommandOptionBuilder().WithName(name).WithDescription(description).WithType(ApplicationCommandOptionType.SubCommand);

        private static SlashCommandOptionBuilder Member() =>
            new SlashCommandOptionBuilder().WithName("member").WithDescription(DiscordText.Commands.OptionMember).WithType(ApplicationCommandOptionType.User).WithRequired(true);

        private static SlashCommandOptionBuilder OptionalUser() =>
            new SlashCommandOptionBuilder().WithName("user").WithDescription(DiscordText.Commands.OptionUserFilter).WithType(ApplicationCommandOptionType.User).WithRequired(false);

        private static SlashCommandOptionBuilder Username() =>
            new SlashCommandOptionBuilder().WithName("username").WithDescription(DiscordText.Commands.OptionUsername)
                .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithMaxLength(100);

        private static SlashCommandOptionBuilder Query() =>
            new SlashCommandOptionBuilder().WithName("query").WithDescription(DiscordText.Commands.OptionQuery)
                .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithMaxLength(100);

        private static SlashCommandOptionBuilder ExternalId(string name, string description) =>
            new SlashCommandOptionBuilder().WithName(name).WithDescription(description)
                .WithType(ApplicationCommandOptionType.Integer).WithRequired(true).WithMinValue(1);

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
