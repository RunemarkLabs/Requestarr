using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord
{
    // Every user-facing Discord string lives here (English only for v1) so a translation
    // can later swap this one file.
    public static class DiscordText
    {
        // Scope and permission
        public const string WrongServer = "Requestarr is not set up for this server.";
        public const string AdminOnly = "Only the Requestarr admin can use this command.";
        public const string AdminChannelOnly = "Admin commands only work in the Requestarr admin channel.";
        public const string NotAUser = "You haven't been added to Requestarr yet. Ask the admin to add you.";
        public const string Suspended = "Your Requestarr access is suspended. Ask the admin if you think this is a mistake.";
        public const string OwnChannelOnly = "Please use this command in your own request channel.";
        public const string AdminUseUserCommand = "In the admin channel, use /user mediaserver to link someone's account.";
        public const string NotConnected = "Requestarr isn't connected to Discord right now. Try again in a minute.";

        // /user add
        public const string MemberNotFound = "That person isn't a member of this server.";
        public const string CannotAddBot = "Bots can't be added as Requestarr users.";
        public const string RoleLimit = "This server is close to Discord's 250-role limit, so no more users can be added. Remove unused roles or users first.";
        public const string AddFailed = "Adding the user failed part way. Nothing was kept; check the Requestarr logs and try again.";

        // Lookups
        public const string NotARequestarrUser = "That member isn't a Requestarr user.";
        public const string UserRemoved = "That user was removed. Add them again with /user add.";
        public const string NotInServer = "That member isn't in the server right now, so they can't be restored yet.";
        public const string TypeNotOffered = "That content type isn't available yet.";
        public const string NoChannel = "Only users with a request channel can link a media server account.";

        // Media server
        public const string AccessRequestAlreadyPending = "You already have a media server access request waiting for the admin.";
        public const string AccessRequestNotFound = "That access request no longer exists.";
        public const string AccessRequestAlreadyDecided = "That access request was already decided.";
        public const string InvalidUsername = "Please give a valid username (up to 100 characters).";

        public static string AlreadyUser(string name, UserStatus status) =>
            status == UserStatus.Suspended
                ? $"{name} is already a Requestarr user (suspended). Use /user unsuspend to restore them."
                : $"{name} is already a Requestarr user.";

        public static string UserAdded(string name, ulong channelId, IEnumerable<UserMediaAccess> access) =>
            $"Added {name}. Their request channel is <#{channelId}>.\n{FormatAccess(access)}";

        public static string Welcome(ulong discordUserId, IEnumerable<UserMediaAccess> access) =>
            $"Welcome <@{discordUserId}>! This is your private request channel. Only you and the admin can see it.\n" +
            $"What you can request:\n{FormatAccess(access)}";

        public static string UserRemovedDone(string name) => $"Removed {name}. Their channel is archived and their request history is kept.";

        public static string UserSuspendedDone(string name) => $"Suspended {name}. Their channel is locked.";

        public static string AlreadySuspended(string name) => $"{name} is already suspended.";

        public static string UserUnsuspendedDone(string name) => $"Restored {name}. Their channel is open again.";

        public static string NotSuspended(string name) => $"{name} isn't suspended.";

        public const string ChannelLockedNotice = "Your Requestarr access is suspended, so this channel is locked.";
        public const string ChannelRestoredNotice = "Your Requestarr access is restored. Welcome back!";
        public const string MemberLeftNotice = "This member left the server. Their access is suspended and the channel is locked.";

        public static string AccessChanged(string name, ContentType type, AccessState state) => $"{name}: {Label(type)} is now {Label(state)}.";

        public static string QuotaChanged(string name, ContentType type, int? limit, int days) =>
            limit.HasValue ? $"{name}: {Label(type)} quota is now {limit} per {days} day{(days == 1 ? string.Empty : "s")}." : $"{name}: {Label(type)} quota removed.";

        public static string UserInfo(RequestarrUser user, UserIdentity identity, IEnumerable<UserMediaAccess> access, IEnumerable<MediaServerAccount> accounts)
        {
            var lines = new List<string>
            {
                $"**{user.DisplayName}** (<@{identity?.ExternalId}>)",
                $"Status: {Label(user.Status)}",
                user.PrivateChannelId != null && user.Status != UserStatus.Removed ? $"Channel: <#{user.PrivateChannelId}>" : "Channel: none",
                $"Added: {user.Added:yyyy-MM-dd}",
                "Access:",
                FormatAccess(access)
            };

            var accountList = accounts.ToList();
            lines.Add(accountList.Any()
                ? "Media server: " + string.Join(", ", accountList.Select(a => $"{a.Provider} `{a.Username}`"))
                : "Media server: none linked");

            return DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength);
        }

        public static string MediaServerLinked(string name, MediaServerProvider provider, string username) => $"Linked {name}'s {provider} account `{username}`.";

        public static string AccessRequestSent(MediaServerProvider provider) => $"Thanks! Your {provider} access request was sent to the admin.";

        public static string AccessRequestCard(string name, ulong discordUserId, MediaServerProvider provider, string username) =>
            $"**Media server access request**\n<@{discordUserId}> ({name}) asks for {provider} access as `{username}`.";

        public static string AccessRequestApproved(string name, MediaServerProvider provider, string username, string admin) =>
            $"**Media server access request: approved** by {admin}\n{name}'s {provider} account `{username}` is linked.";

        public static string AccessRequestDenied(string name, MediaServerProvider provider, string username, string admin) =>
            $"**Media server access request: denied** by {admin}\n{name} asked for {provider} access as `{username}`.";

        public static string AccessRequestApprovedUser(MediaServerProvider provider, string username) =>
            $"Your {provider} access request for `{username}` was approved. Your account is linked; the server invite follows in a later release.";

        public static string AccessRequestDeniedUser(MediaServerProvider provider) => $"Your {provider} access request was declined by the admin.";

        public const string ApproveLabel = "Approve";
        public const string DenyLabel = "Deny";

        public static string FormatAccess(IEnumerable<UserMediaAccess> access)
        {
            return string.Join("\n", access
                .Where(a => ContentTypes.IsOffered(a.ContentType))
                .OrderBy(a => a.ContentType)
                .Select(a => $"• {Label(a.ContentType)}: {Label(a.State)}{(a.QuotaLimit.HasValue ? $" (quota {a.QuotaLimit} per {(int)(a.QuotaWindow ?? TimeSpan.FromDays(7)).TotalDays} days)" : string.Empty)}"));
        }

        public static string Label(ContentType type) => type switch
        {
            ContentType.Movie => "Movies",
            ContentType.Tv => "TV",
            ContentType.Music => "Music",
            ContentType.Comic => "Comics",
            ContentType.Book => "Books",
            _ => type.ToString()
        };

        public static string Label(AccessState state) => state switch
        {
            AccessState.Off => "off",
            AccessState.NeedsApproval => "needs approval",
            AccessState.AutoApprove => "auto-approve",
            _ => state.ToString()
        };

        public static string Label(UserStatus status) => status switch
        {
            UserStatus.Active => "active",
            UserStatus.Suspended => "suspended",
            UserStatus.Removed => "removed",
            _ => status.ToString()
        };

        // Slash command descriptions (Discord: up to 100 characters)
        public static class Commands
        {
            public const string User = "Manage Requestarr users (admin)";
            public const string UserAdd = "Add a server member as a Requestarr user";
            public const string UserRemove = "Remove a user; their channel is archived and history kept";
            public const string UserSuspend = "Suspend a user and lock their channel";
            public const string UserUnsuspend = "Restore a suspended user";
            public const string UserAccess = "Set what a user may request for one content type";
            public const string UserQuota = "Set a user's request quota for one content type";
            public const string UserInfo = "Show a user's Requestarr settings";
            public const string UserMediaServer = "Link a user's media server account";
            public const string MediaServer = "Media server access";
            public const string MediaServerRequest = "Ask the admin for media server access";

            public const string OptionMember = "The server member";
            public const string OptionType = "Content type";
            public const string OptionLevel = "Access level";
            public const string OptionLimit = "Requests allowed per window (0 removes the quota)";
            public const string OptionDays = "Window length in days (default 7)";
            public const string OptionService = "Media server";
            public const string OptionUsername = "Media server username";
            public const string OptionMovie = "Movies access (default: needs approval)";
            public const string OptionTv = "TV access (default: needs approval)";
            public const string OptionMusic = "Music access (default: off)";
        }
    }
}
