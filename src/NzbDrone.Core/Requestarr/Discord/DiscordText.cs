using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
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

        public static string UserRemovedDone(string name) => $"Removed {name}. Their channel is archived and their request history is kept.";

        public static string UserSuspendedDone(string name) => $"Suspended {name}. Their channel is locked.";

        public static string AlreadySuspended(string name) => $"{name} is already suspended.";

        public static string UserUnsuspendedDone(string name) => $"Restored {name}. Their channel is open again.";

        public static string NotSuspended(string name) => $"{name} isn't suspended.";

        public const string ChannelLockedNotice = "Your Requestarr access is suspended, so this channel is locked.";
        public const string ChannelRestoredNotice = "Your Requestarr access is restored. Welcome back!";
        public const string MemberLeftNotice = "This member left the server. Their access is suspended and the channel is locked.";

        public static string AccessChanged(string name, ContentType type, AccessState state, string instance = null) =>
            $"{name}: {Label(type)} is now {Label(state)}{(instance != null ? $", using {instance}" : string.Empty)}.";

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

        // Requesting
        public const string MusicNotYet = "Music requests aren't available yet.";
        public const string RequestNotFound = "That request doesn't exist.";
        public const string NotYourRequest = "You can only cancel your own requests.";
        public const string NothingFound = "Nothing found. Try different words, or request by ID.";
        public const string TitleNotFound = "That title wasn't found.";
        public const string ChooseSeasons = "Pick at least one season, or future seasons.";
        public const string AlreadyRequestedByYou = "You've already requested this.";
        public const string AlreadyFollowing = "You're already following this request.";
        public const string AlreadyInLibrary = "This is already in the library, so there's nothing to request.";
        public const string RequestLabel = "Request";
        public const string FollowLabel = "Follow request";
        public const string SearchPlaceholder = "Pick a title";
        public const string SeasonsPlaceholder = "Pick the seasons you want";
        public const string AllSeasons = "All seasons";
        public const string ChooseSeasonsLabel = "Choose seasons";
        public const string ChooseSeasonsPrompt = "Pick the seasons you want. Add \"Also new seasons as they air\" to keep getting new ones.";
        public const string Specials = "Specials";
        public const string SpecialsHint = "Season 0: extras and one-off episodes";
        public const string AlsoFutureSeasons = "Also new seasons as they air";
        public const string AlsoFutureSeasonsHint = "Sonarr monitors seasons that haven't aired yet";
        public const string BackLabel = "Back";
        public const string BackToResults = "Back to results";
        public const string InLibraryShort = "In library";
        public const string Dot = " · ";

        // Steps of a request block, shown after its header.
        public const string StepDetails = "details";
        public const string StepSeasons = "choose seasons";
        public const string StepConfirm = "confirm";
        public const string StepStopped = "stopped";

        // Final status of a request block.
        public const string FinalSent = "✅ **Request sent**";
        public const string FinalPending = "⏳ **Waiting for approval**";
        public const string FinalFollowing = "📌 **Following**";
        public const string FinalAvailable = "✅ **Already available**";
        public const string FinalFailed = "⚠️ **Couldn't be added**";
        public const string FinalRefused = "⛔ **Not requested**";
        public const string FinalCancelled = "🚫 **Cancelled**";

        // The Movie Database attribution, wherever its data appears.
        public const string TmdbAttribution = "Cast from The Movie Database (TMDB). This product uses the TMDB API but is not endorsed or certified by TMDB.";
        public const string TmdbSubtext = "-# " + TmdbAttribution;

        // Card fields
        public const string FieldGenres = "Genres";
        public const string FieldRating = "Rating";
        public const string FieldCertification = "Rated";
        public const string FieldRuntime = "Runtime";
        public const string FieldNetwork = "Network";
        public const string FieldStudio = "Studio";
        public const string FieldStatus = "Status";
        public const string FieldSeasons = "Seasons";
        public const string FieldEpisodes = "Episodes";
        public const string FieldAired = "Aired";
        public const string FieldReleased = "Released";
        public const string FieldCast = "Cast";
        public const string FieldGoesTo = "Goes to";

        public static string BlockHeader(ContentType type, string step) => $"{(type == ContentType.Tv ? "📺" : "🎬")} **{Label(type)} request**{Dot}{step}";

        public static string SeasonLabel(int season) => $"Season {season}";

        public static string YouChose(string choice) => $"You chose **{choice}**.\nPress **Request** to send it, or **Back** to change it.";

        public static string SeasonsLine(string choice) => $"Seasons: {choice}.";

        // Plain words for a season choice, so the confirmation leaves no guesswork.
        public static string SeasonChoiceText(SeasonChoice choice, IReadOnlyCollection<int> available)
        {
            if (choice.All)
            {
                return available.Any()
                    ? $"all seasons ({Ranges(available)}) and new seasons as they air, without specials"
                    : "all seasons, including new seasons as they air, without specials";
            }

            return SeasonList(choice.Seasons, choice.Future);
        }

        public static string SeasonList(IReadOnlyCollection<int> seasons, bool future)
        {
            var regular = seasons.Where(s => s > 0).Distinct().OrderBy(s => s).ToList();
            var parts = new List<string>();

            if (seasons.Contains(0))
            {
                parts.Add("specials");
            }

            if (regular.Any())
            {
                parts.Add($"{(regular.Count == 1 ? "season" : "seasons")} {Ranges(regular)}");
            }

            if (!parts.Any())
            {
                return future ? "only new seasons as they air (no existing seasons)" : "no seasons";
            }

            var list = string.Join(" and ", parts);

            return future ? $"{list}, plus new seasons as they air" : $"{list} only; new seasons won't be added";
        }

        // 1, 2, 3, 5 -> "1–3 and 5"
        public static string Ranges(IEnumerable<int> numbers)
        {
            var sorted = numbers.Distinct().OrderBy(n => n).ToList();
            var ranges = new List<string>();

            for (var i = 0; i < sorted.Count; i++)
            {
                var start = sorted[i];

                while (i + 1 < sorted.Count && sorted[i + 1] == sorted[i] + 1)
                {
                    i++;
                }

                ranges.Add(start == sorted[i] ? $"{start}" : $"{start}–{sorted[i]}");
            }

            return ranges.Count <= 1 ? string.Join(string.Empty, ranges) : string.Join(", ", ranges.Take(ranges.Count - 1)) + " and " + ranges.Last();
        }

        public static string Rating(Backends.MediaLookupResult item)
        {
            if (!item.Ratings.Any())
            {
                return null;
            }

            if (item.Ratings.Count == 1 && item.Ratings[0].Source == "Rating")
            {
                var value = item.Ratings[0].Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                return item.Votes.HasValue ? $"{value} ({item.Votes.Value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} votes)" : value;
            }

            return string.Join(Dot, item.Ratings.Select(r => r.Source == "Rotten Tomatoes"
                ? $"{r.Source} {r.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}%"
                : $"{r.Source} {r.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"));
        }

        public static string Runtime(int minutes, bool perEpisode)
        {
            var text = minutes >= 60 ? $"{minutes / 60} h{(minutes % 60 > 0 ? $" {minutes % 60} min" : string.Empty)}" : $"{minutes} min";
            return perEpisode ? $"{text} per episode" : text;
        }

        public static string SeasonCount(int seasons, bool specials)
        {
            if (seasons == 0)
            {
                return specials ? "specials only" : null;
            }

            return specials ? $"{seasons} + specials" : seasons.ToString();
        }

        public static string Aired(DateTime? first, DateTime? last, bool continuing)
        {
            var lines = new List<string>();

            if (first.HasValue)
            {
                lines.Add($"First {Day(first.Value)}");
            }

            if (last.HasValue && !continuing)
            {
                lines.Add($"Last {Day(last.Value)}");
            }

            return lines.Any() ? string.Join("\n", lines) : null;
        }

        public static string Released(DateTime? cinemas, DateTime? digital, DateTime? physical)
        {
            var lines = new List<string>();

            if (cinemas.HasValue)
            {
                lines.Add($"In cinemas {Day(cinemas.Value)}");
            }

            if (digital.HasValue)
            {
                lines.Add($"Digital {Day(digital.Value)}");
            }

            if (physical.HasValue)
            {
                lines.Add($"Physical {Day(physical.Value)}");
            }

            return lines.Any() ? string.Join("\n", lines) : null;
        }

        public static string Day(DateTime date) => date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

        // "inCinemas" -> "In cinemas", "continuing" -> "Continuing"
        public static string Capitalize(string status)
        {
            if (string.IsNullOrWhiteSpace(status) || status == "deleted" || status == "tba")
            {
                return null;
            }

            var words = System.Text.RegularExpressions.Regex.Replace(status, "(?<=[a-z])([A-Z])", " $1").ToLowerInvariant();
            return char.ToUpperInvariant(words[0]) + words.Substring(1);
        }

        public const string DenyReasonTitle = "Deny request";
        public const string DenyReasonLabel = "Reason (shown to the user)";
        public const string NoReason = "no reason given";
        public const string NoRequests = "No requests match.";
        public const string QueueEmpty = "Nothing is waiting for approval.";
        public const string StateInLibrary = "✅ Already in the library.";

        public static string AccessOff(ContentType type) => $"You can't request {Label(type).ToLowerInvariant()}. Ask the admin if you'd like access.";

        public static string NoBackend(ContentType type) => $"No {(type == ContentType.Tv ? "Sonarr" : "Radarr")} is set up for {Label(type).ToLowerInvariant()} yet. Ask the admin.";

        public static string BackendUnavailable(string instance) => $"{instance} isn't reachable right now. Try again later.";

        public static string SearchResults(ContentType type, string query, int count) =>
            BlockHeader(type, $"{count} result{(count == 1 ? string.Empty : "s")} for \"{query}\"") + "\nPick one to see the details.";

        public static string StateRequested(string owner, RequestStatus status, int id) => $"📌 Already requested by {owner} (#{id}, {Label(status)}). You can follow it to be notified.";

        public static string StateYours(RequestStatus status, int id) => $"📌 You requested this (#{id}, {Label(status)}).";

        public static string StateFollowing(int id) => $"📌 You're following request #{id}.";

        public static string StatePreviouslyDenied(string reason) => $"⛔ Previously denied: {reason ?? NoReason}. You can request it again.";

        public static string Subscribed(string label, string owner, int id) => $"{owner} already requested {label} (#{id}). You'll be notified when it's available.";

        public static string QuotaReached(ContentType type, int limit, int days, DateTime nextSlot) =>
            $"You've reached your {Label(type).ToLowerInvariant()} limit ({limit} per {days} days). Your next request frees up {DiscordTimestamp(nextSlot)}.";

        public static string RequestPending(string label, int id) => $"Request #{id} for {label} is waiting for the admin's approval.";

        public static string RequestSent(string label, int id, string instance) => $"Request #{id} for {label} was added to {instance} and a search has started.";

        public static string RequestApprovedNotice(string label, int id, string instance) => $"Your request #{id} for {label} was approved and added to {instance}. A search has started.";

        public static string RequestDeniedNotice(string label, int id, string reason) => $"Your request #{id} for {label} was denied: {reason ?? NoReason}.";

        public static string RequestFailedNotice(string label, int id, string error) => $"Request #{id} for {label} couldn't be added: {error}";

        public static string RequestCancelled(string label, int id) => $"Request #{id} for {label} was cancelled.";

        public static string CannotCancel(RequestStatus status) => $"Only pending requests can be cancelled; this one is {Label(status)}.";

        public static string NotPending(int id, RequestStatus status) => $"Request #{id} is {Label(status)}, not waiting for approval.";

        public static string ApprovalCard(Request request, string owner, string instance) =>
            $"**Request #{request.Id}** · {Label(request.ContentType)} · {request.Label}\nRequested by {owner} for {instance}" +
            (request.ContentType == ContentType.Tv ? $"\nSeasons: {Seasons(request)}" : string.Empty);

        public static string ApprovedCard(Request request, string owner, string admin) => $"**Request #{request.Id}: approved** by {admin} · {request.Label} for {owner}";

        public static string DeniedCard(Request request, string owner, string admin, string reason) => $"**Request #{request.Id}: denied** by {admin} · {request.Label} for {owner} · {reason ?? NoReason}";

        public static string AdminApproved(Request request, string outcome) => $"Approved request #{request.Id} for {request.Label}. {outcome}";

        public static string AdminDenied(Request request) => $"Denied request #{request.Id} for {request.Label}.";

        public static string Seasons(Request request) => SeasonList(request.Seasons, request.MonitorFutureSeasons);

        public static string RequestLine(Request request, string owner, bool showOwner) =>
            $"#{request.Id} · {Label(request.ContentType)} · {request.Label} · {Label(request.Status)}{(showOwner ? " · " + owner : string.Empty)}";

        public static string QuotaLine(ContentType type, AccessState state, int used, int? limit, int days, DateTime? nextSlot) =>
            $"• {Label(type)}: {Label(state)}" + (limit.HasValue
                ? $", {used} of {limit} used in the last {days} days{(used >= limit && nextSlot.HasValue ? $", next slot {DiscordTimestamp(nextSlot.Value)}" : string.Empty)}"
                : ", no limit");

        public static string Label(RequestStatus status) => status.ToString().ToLowerInvariant();

        public static string DiscordTimestamp(DateTime utc) => $"<t:{new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds()}:R>";

        public static string InstanceNotFound(string name, ContentType type) => $"No enabled {(type == ContentType.Tv ? "Sonarr" : "Radarr")} instance named '{name}'.";

        // Panels and the single /request command
        public const string PanelMyRequests = "My requests";
        public const string PanelMyLimits = "My limits";
        public const string PanelLinkPlex = "Link Plex";
        public const string PanelHelp = "Help";
        public const string PanelPendingQueue = "Pending queue";
        public const string PanelUsers = "Users";
        public const string AdminPanel = "**Requestarr admin**\nApproval cards for requests and media server access appear in this channel.";
        public const string LinkPlexTitle = "Link your Plex account";
        public const string LinkPlexLabel = "Your Plex username or email";
        public const string CancelLabel = "Cancel request";
        public const string NoUsers = "No Requestarr users yet. Add someone with /admin user add.";
        public const string MissingPinMessages = "The Discord bot is missing the Pin Messages permission, so panels are posted but not pinned. Turn on Pin Messages for the bot's role in Server Settings, Roles.";
        public const string NothingToRequest = "You can't request anything yet. Ask the admin for access.";
        public const string ChooseType = "Pick a type from the list (start typing in the type option).";
        public const string TmdbIsForMovies = "tmdb: IDs are for movies. Pick the Movies type, or use tvdb: for TV.";
        public const string TvdbIsForTv = "tvdb: IDs are for TV. Pick the TV type, or use tmdb: for movies.";

        // The one pinned message in a user's channel: welcome, real access, buttons. No how-to text.
        public static string UserPanel(string discordUserId, string name, IEnumerable<UserMediaAccess> allowed)
        {
            var list = allowed.OrderBy(a => a.ContentType).Select(a => $"{Label(a.ContentType)} ({Label(a.State)})").ToList();
            var can = list.Any() ? "You can request: " + string.Join(", ", list) + "." : NothingToRequest;
            var hello = discordUserId != null ? $"Welcome <@{discordUserId}>! " : string.Empty;

            return $"**Requestarr · {name}**\n{hello}This is your private request channel; only you and the admins can see it.\n{can}";
        }

        public static string UserLine(RequestarrUser user, IEnumerable<UserMediaAccess> access) =>
            $"• {user.DisplayName} · {Label(user.Status)}{(user.PrivateChannelId != null && user.Status != UserStatus.Removed ? $" · <#{user.PrivateChannelId}>" : string.Empty)} · " +
            string.Join(", ", access.Where(a => ContentTypes.IsOffered(a.ContentType)).OrderBy(a => a.ContentType).Select(a => $"{Label(a.ContentType)}: {Label(a.State)}"));

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
            public const string Admin = "Requestarr admin commands";
            public const string AdminUser = "Manage Requestarr users";
            public const string AdminMediaServer = "Media server access requests";
            public const string RequestOne = "Request a movie or TV show";
            public const string Help = "How to use Requestarr";
            public const string AdminHelp = "Help for admins";
            public const string OptionRequestType = "What to request (only types you have access to)";
            public const string OptionRequestQuery = "Title to search for, or tmdb:603 / tvdb:12345";
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

            public const string Queue = "Show requests waiting for approval (admin)";
            public const string Approve = "Approve a pending request (admin)";
            public const string Deny = "Deny a pending request (admin)";
            public const string Request = "Request movies and TV";
            public const string RequestMovie = "Search for a movie to request";
            public const string RequestTv = "Search for a TV show to request";
            public const string RequestMovieTmdb = "Request a movie by its TMDB ID";
            public const string RequestTvTvdb = "Request a TV show by its TVDB ID";
            public const string RequestCancel = "Cancel one of your pending requests";
            public const string Requests = "List requests";
            public const string Quota = "Show request limits and usage";
            public const string MediaServerApprove = "Approve a media server access request (admin)";
            public const string MediaServerDeny = "Deny a media server access request (admin)";
            public const string OptionInstance = "Backend instance by name (default: the type's default)";
            public const string OptionReason = "Reason shown to the user";
            public const string OptionUserFilter = "Only this user (admin)";
            public const string OptionQuery = "Title to search for";
            public const string OptionTmdbId = "TMDB movie ID";
            public const string OptionTvdbId = "TVDB series ID";
            public const string OptionRequestId = "Request number";
            public const string OptionStatus = "Request status";

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
