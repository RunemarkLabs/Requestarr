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
        public const string AlreadyInLibrary = "Good news: it's already in the library, ready for you. No request needed.";
        public const string RequestLabel = "Request";
        public const string FollowLabel = "Follow request";
        public const string SearchPlaceholder = "Pick a title";
        public const string SeasonsPlaceholder = "Pick the seasons you want";
        public const string AllSeasons = "All seasons";
        public const string ChooseSeasonsLabel = "Choose seasons";
        public const string ChooseSeasonsPrompt = "Pick the seasons you want, then press Continue. Turn on \"Also new seasons\" to keep getting new ones.";
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
        public const string FinalAvailable = "🎉 **Already available**";
        public const string FinalFailed = "⚠️ **Couldn't be added**";
        public const string FinalRefused = "⛔ **Not requested**";
        public const string FinalCancelled = "🚫 **Cancelled**";
        public const string FinalApproved = "✅ **Approved**";
        public const string FinalDenied = "⛔ **Denied**";

        // Late clicks on request messages: nothing changes, the asker is told why.
        public const string NothingToFollow = "That request isn't active any more, so there's nothing to follow. The message now shows the title as it is.";

        public static string NothingChanged(string state) => $"Nothing was changed. {state}";

        public static string RequestState(Request request) => $"Request #{request.Id} for {request.Label} is {Label(request.Status)}.";

        public static string RequestApprovedStatus(string label, int id, string instance) => $"Request #{id} for {label} was approved and added to {instance ?? "the server"}. A search has started.";

        public const string AlreadyAvailableLabel = "Already available";

        public static string RequestAlreadyAvailableStatus(string label, int id) => $"Good news: {label} is already in the library, ready for you. Request #{id} is complete.";

        public static string RequestAlreadyAvailableNotice(string label, int id) => $"🎉 Good news! {label} (request #{id}) is already in the library, ready for you.";

        public const string AlreadyThereNote = "🎉 Good news: it's already in the library, ready for you. Nothing to request.";

        public static string AdminMarkedAvailable(Request request) => $"Request #{request.Id} for {request.Label} is closed as already available. The user is told the good news.";

        public static string RequestDeniedStatus(string label, int id, string reason) => $"Request #{id} for {label} was denied: {reason ?? NoReason}.";

        public static string RequestCancelledByAdmin(string label, int id, string reason) => $"Request #{id} for {label} was cancelled by the admin{(reason != null ? ": " + reason : string.Empty)}.";

        public static string RequestCancelledByAdminNotice(string label, int id, string reason) => $"Your request #{id} for {label} was cancelled by the admin{(reason != null ? ": " + reason : string.Empty)}.";

        public static string AdminCancelled(Request request) => $"Cancelled request #{request.Id} for {request.Label}.";

        // The approval card once the request is decided, without its buttons.
        public static string DecidedCard(Request request, string owner, string by) => request.Status switch
        {
            RequestStatus.Approved => ApprovedCard(request, owner, by ?? "the admin"),
            RequestStatus.Available => $"**Request #{request.Id}: already available** · {request.Label} for {owner}{(by != null ? $" · closed by {by}" : string.Empty)}",
            RequestStatus.Denied => DeniedCard(request, owner, by ?? "the admin", request.DecisionReason),
            RequestStatus.Cancelled when by != null => $"**Request #{request.Id}: cancelled** by {by} · {request.Label} for {owner}",
            RequestStatus.Cancelled => $"**Request #{request.Id}: cancelled** by {owner} · {request.Label}",
            RequestStatus.Failed => $"**Request #{request.Id}: couldn't be added** · {request.Label} for {owner} · {request.DecisionReason}",
            _ => $"**Request #{request.Id}** · {request.Label} for {owner} · {Label(request.Status)}"
        };

        // Choose seasons, a page at a time.
        public const string NothingPickedYet = "Nothing picked yet.";
        public const string ContinueLabel = "Continue";

        public static string PickedSoFar(string choice) => $"Picked so far: **{choice}**.";

        public static string SeasonPageHint((int From, int To) page, int total) =>
            $"Showing seasons {page.From}–{page.To} of {total}. Use the arrows for the others; your picks are kept.";

        public static string PreviousSeasons((int From, int To) page) => $"◀ Seasons {page.From}–{page.To}";

        public static string NextSeasons((int From, int To) page) => $"Seasons {page.From}–{page.To} ▶";

        public static string FutureToggle(bool on) => $"Also new seasons: {(on ? "on" : "off")}";

        // The Movie Database: a short credit wherever its data appears, the full notice in Help and the docs.
        public const string TmdbAttribution = "Cast from TMDB";
        public const string TmdbSubtext = "-# " + TmdbAttribution;
        public const string TmdbNotice = "Cast and TV episode counts come from The Movie Database (TMDB). Requestarr uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.";

        // TMDB's approved "alt short" logo, rendered unaltered to PNG (Discord can't show SVG) and
        // served from this project's public repository. Shown small: never larger than Requestarr's own mark.
        public const string TmdbLogoUrl = DiscordAssets.Scheme + DiscordAssets.TmdbLogo;
        public const string TmdbName = "The Movie Database (TMDB)";
        public const string TmdbSite = "https://www.themoviedb.org";

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
        public const string StateInLibrary = "🎉 Good news: this is already in the library, ready for you.";

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

        // Anime: sub or dub
        public static string Label(AnimeLanguage language) => language == AnimeLanguage.Dub ? "Dub" : "Sub";

        public static string LanguageHint(AnimeLanguage language) => language == AnimeLanguage.Dub ? "English audio" : "original audio with subtitles";

        public const string AnimeIntro = "This is anime. Pick **Dub** (English audio) or **Sub** (original audio with subtitles).";

        public static string SwitchLabel(AnimeLanguage language) => $"Switch to {Label(language)}";

        public static string LanguageLine(AnimeLanguage language) => $"Language: **{Label(language)}** ({LanguageHint(language)})";

        public static string CurrentlySetTo(AnimeLanguage language) => $"Set to **{Label(language)}** here.";

        public static string PriorityRule(Anime.AnimePriority priority, string instance) => priority switch
        {
            Anime.AnimePriority.Dub => $"On {instance}, dub wins: a dub request switches a sub series to dub, and a sub request for a dub series follows it.",
            Anime.AnimePriority.Sub => $"On {instance}, sub wins: a sub request switches a dub series to sub, and a dub request for a sub series follows it.",
            _ => $"On {instance}, the first request decides: a series stays in the language it was first requested in."
        };

        private static string Keeps(Anime.AnimePriority priority) => priority switch
        {
            Anime.AnimePriority.Dub => "dub wins here",
            Anime.AnimePriority.Sub => "sub wins here",
            _ => "the first request decides here"
        };

        public static string AnimeStays(string label, AnimeLanguage existing, Anime.AnimePriority priority, string owner, int id) =>
            $"{label} stays **{Label(existing)}** ({Keeps(priority)}). You're following {owner}'s request #{id} and will be told when it's added.";

        public static string AnimeStaysYours(string label, AnimeLanguage existing, Anime.AnimePriority priority) =>
            $"Your request for {label} stays **{Label(existing)}** ({Keeps(priority)}).";

        public static string AnimeStaysInLibrary(string label, AnimeLanguage existing, Anime.AnimePriority priority, Requests.LibraryStanding standing) =>
            $"{Standing(label, standing)}, set to **{Label(existing)}** ({Keeps(priority)}).";

        // Where a series already in Sonarr stands, in the words the anime replies use.
        public static string Standing(string label, Requests.LibraryStanding standing) => standing switch
        {
            Requests.LibraryStanding.Available => $"Good news: {label} is already in the library",
            Requests.LibraryStanding.PartlyAvailable => $"{label} is partly in the library (more is on its way)",
            Requests.LibraryStanding.Downloading => $"{label} is downloading",
            _ => $"{label} was sent to Sonarr and nothing is downloaded yet"
        };

        public static string StandingHeader(Requests.LibraryStanding standing) => standing switch
        {
            Requests.LibraryStanding.Available => FinalAvailable,
            Requests.LibraryStanding.PartlyAvailable => FinalPartlyAvailable,
            Requests.LibraryStanding.Downloading => FinalDownloading,
            _ => FinalSearching
        };

        public const string FinalSearching = "🔎 **Sent, nothing downloaded yet**";

        // Honest about a switch: Sonarr changes what it looks for next; files already there stay.
        public static string AnimeSwitchedNote(AnimeLanguage language) =>
            $"🔁 Now set to **{Label(language)}** after a later request. Episodes already downloaded stay as they are; new and missing episodes will be looked for as {Label(language)}.";

        public static string AnimeSwitchedNotice(string label, AnimeLanguage language) => $"🔁 {label} is now set to **{Label(language)}** after a later request. Episodes already downloaded stay as they are; new and missing episodes will be looked for as {Label(language)}.";

        public static string AnimeSwitchedInLibrary(string label, AnimeLanguage language, Requests.LibraryStanding standing) =>
            $"{Standing(label, standing)}, and is now set to **{Label(language)}**. " + (standing == Requests.LibraryStanding.Searching
                ? $"It will be looked for as {Label(language)}."
                : $"New and missing episodes will be looked for as {Label(language)}; episodes already downloaded stay as they are.");

        public static string AnimeSwitchedYours(string label, AnimeLanguage language) => $"Your request for {label} is now **{Label(language)}**.";

        public static string AnimeSwitchedFollow(string label, AnimeLanguage language, string owner, int id) =>
            $"{label} is now set to **{Label(language)}**. You're following {owner}'s request #{id} and will be told when it's added.";

        public static string FollowingOf(string owner) => $"You're following {owner}'s request.";

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
        public const string NoUsers = "No Requestarr users yet. Add someone with Add or find a member.";
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
        // Availability: what Sonarr / Radarr are doing with a sent request.
        public const string FinalDownloading = "⬇️ **Downloading**";
        public const string FinalStalled = "⚠️ **Download stuck**";
        public const string FinalDownloadFailed = "⚠️ **Download failed**";
        public const string FinalReady = "🎉 **Available**";
        public const string FinalPartlyAvailable = "🎉 **Partly available**";

        // Seasons as a plain list ("specials and seasons 1–3"), without the new-seasons wording.
        public static string PlainSeasons(IReadOnlyCollection<int> seasons)
        {
            var regular = (seasons ?? Array.Empty<int>()).Where(s => s > 0).Distinct().OrderBy(s => s).ToList();
            var parts = new List<string>();

            if (seasons?.Contains(0) == true)
            {
                parts.Add("specials");
            }

            if (regular.Any())
            {
                parts.Add($"{(regular.Count == 1 ? "season" : "seasons")} {Ranges(regular)}");
            }

            return parts.Any() ? string.Join(" and ", parts) : "nothing yet";
        }

        public static string RequestSearchingStatus(Request request, string instance) =>
            $"Request #{request.Id} for {request.Label} is in {instance ?? "the server"}, which is looking for a release.";

        public static string RequestDownloadingStatus(Request request, string instance) =>
            $"Request #{request.Id} for {request.Label} is downloading in {instance ?? "the server"}.";

        public static string RequestStalledStatus(Request request, string instance) =>
            $"Request #{request.Id} for {request.Label} stopped downloading in {instance ?? "the server"}{Because(request.ProgressDetail)}. The admin can see this too.";

        public static string RequestDownloadFailedStatus(Request request, string instance) =>
            $"The download for request #{request.Id} ({request.Label}) failed{Because(request.ProgressDetail)}. {instance ?? "The server"} usually tries another release by itself.";

        public static string RequestReadyStatus(Request request) => request.ContentType == ContentType.Tv
            ? $"Good news: {request.Label} is ready to watch: {PlainSeasons(request.AvailableSeasons)}. Request #{request.Id} is complete."
            : $"Good news: {request.Label} is ready to watch. Request #{request.Id} is complete.";

        public static string AvailableSoFar(Request request) => $"Available now: **{PlainSeasons(request.AvailableSeasons)}**. The rest is still on its way.";

        public static string RequestFulfilledNotice(string label, int id) => $"🎉 Good news! {label} (request #{id}) is ready to watch.";

        // Fallback notices, for people with no request message to repost.
        public static string RequestProgressNotice(Request request, string instance) => request.Progress switch
        {
            DownloadProgress.Downloading when request.AvailableSeasons?.Any() == true => $"{request.Label} (request #{request.Id}): {PlainSeasons(request.AvailableSeasons)} available now; the rest is downloading.",
            DownloadProgress.Downloading => $"{request.Label} (request #{request.Id}) is downloading in {instance ?? "the server"}.",
            DownloadProgress.Stalled => $"{request.Label} (request #{request.Id}) stopped downloading{Because(request.ProgressDetail)}.",
            DownloadProgress.Failed => $"The download for {request.Label} (request #{request.Id}) failed{Because(request.ProgressDetail)}; {instance ?? "the server"} usually tries another release.",
            _ when request.AvailableSeasons?.Any() == true => $"{request.Label} (request #{request.Id}): {PlainSeasons(request.AvailableSeasons)} available now.",
            _ => $"{request.Label} (request #{request.Id}) is waiting for a release in {instance ?? "the server"}."
        };

        public static string NoProgressFor(int hours) => $"no progress for {hours} hours";

        public static string WebhookUnreachable(string url, string error, int minutes) =>
            $"can't reach Requestarr at {url} ({error}). Downloads are still tracked: Requestarr checks every {minutes} minutes.";

        private static string Because(string detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}";

        // Cancelling a request that was already sent (admin, from its feed or request card).
        public static string CannotCancelDownloading(int id) =>
            $"Request #{id} is already downloading or partly there, so it can't be cancelled any more. Removing titles comes with removal votes.";

        public static string CancelNotStopped(Request request, string instance) =>
            $"Request #{request.Id} wasn't cancelled: {instance ?? "the server"} couldn't be told to stop searching for it. Nothing changed; try again in a moment.";

        public static string AdminCancelledSent(Request request, string instance) =>
            $"Cancelled request #{request.Id} for {request.Label}. It stays in {instance} but is no longer monitored there, so nothing more is searched for or downloaded. Nothing was deleted.";

        // Adding seasons to a TV request.
        public const string AddSeasonsLabel = "Add seasons";
        public const string AddTheseSeasonsLabel = "Add these seasons";
        public const string AddSeasonsPrompt = "Pick the seasons to add. Adding needs no approval and doesn't count against limits.";
        public const string EverythingRequested = "Every season is already requested, and new seasons are added as they air.";
        public const string NothingNewToAdd = "Those seasons are already requested; nothing was added.";

        public static string AddSeasonsHeader(Request request) => $"📺 **Add seasons**{Dot}{request.Label}";

        public static string AlreadyRequested(Request request) =>
            $"Already requested: **{SeasonList(request.Seasons, request.MonitorFutureSeasons)}**{(request.AvailableSeasons?.Any() == true ? $" ({PlainSeasons(request.AvailableSeasons)} available)" : string.Empty)}.";

        public static string CannotAddSeasons(RequestStatus status) => $"Seasons can only be added to an open request; this one is {Label(status)}.";

        public static string AddedSeasons(IReadOnlyCollection<int> seasons, bool future) =>
            seasons.Any() ? $"{PlainSeasons(seasons)}{(future ? ", plus new seasons as they air" : string.Empty)}" : "new seasons as they air";

        public static string SeasonsAdded(Request request, string added, bool pending) => pending
            ? $"Added {added} to request #{request.Id}. They go to Sonarr with the request once it's approved."
            : $"Added {added} to request #{request.Id}. Sonarr is searching for them now.";

        public static string SeasonsAddedNotice(string label, int id, string added) => $"{Capitalize(added)} added to request #{id} for {label}.";

        public static string SeasonsAddedNote(string added) => $"➕ {Capitalize(added)} added.";

        // The admin feed: one compact card per request.
        // A transparent 800x1 image: Discord sizes embeds to their content, so every feed card uses it
        // to be drawn the same width.
        public const string FeedCardWidthUrl = DiscordAssets.Scheme + DiscordAssets.FeedCardWidth;

        public const string FeedChannelTopic = "Requestarr activity: one card per request, newest at the bottom. Admins only.";
        public const string AnnouncementsChannelTopic = "News from Requestarr for everyone: new things you can do, and outages that affect requests. Only admins post here.";

        public static string FeedState(Request request) => request.Status switch
        {
            RequestStatus.Pending => FinalPending,
            RequestStatus.Approved => request.Progress switch
            {
                DownloadProgress.Downloading => request.AvailableSeasons?.Any() == true ? $"{FinalPartlyAvailable}, rest downloading" : FinalDownloading,
                DownloadProgress.Stalled => $"{FinalStalled}{Because(request.ProgressDetail)}",
                DownloadProgress.Failed => $"{FinalDownloadFailed}{Because(request.ProgressDetail)}",
                _ => request.AvailableSeasons?.Any() == true ? FinalPartlyAvailable : "✅ **Sent**, searching"
            },
            RequestStatus.Available => request.Progress == DownloadProgress.Imported ? FinalReady : FinalAvailable,
            RequestStatus.Denied => $"{FinalDenied}{Because(request.DecisionReason)}",
            RequestStatus.Cancelled => FinalCancelled,
            RequestStatus.Removed => FinalRemoved,
            _ => $"{FinalFailed}{Because(request.DecisionReason)}"
        };

        public static string FeedMeta(Request request, string owner, string instance) =>
            $"{(request.ContentType == ContentType.Tv ? "📺 TV" : "🎬 Movie")}{Dot}{owner}{(instance != null ? Dot + instance : string.Empty)}";

        public static string FeedFollowers(IReadOnlyCollection<string> names) => $"Following: {string.Join(", ", names)}";

        public static string FeedFooter(Request request) => $"Request #{request.Id}";

        // Account events go to the admin channel (never the feed).
        public static string MemberLeftAdminNotice(string name) =>
            $"**{name}** left the server: their access is suspended and their channel is locked. **Unsuspend** on their card (`/admin user`) restores them if they come back.";

        // Removals: the requester asked first (admin-initiated, owned), then a "still watching?" vote
        // for everyone with access to the type. Nothing changes until it's decided.
        public const string RemovalYesLabel = "Yes, remove it";
        public const string RemovalNoLabel = "No, keep it";
        public const string RemovalKeepLabel = "Keep it";
        public const string RemovalTakeBackLabel = "Take back my keep";
        public const string RemoveRequestLabel = "Remove…";
        public const string DeleteLabel = "Delete";
        public const string ForceRemovalLabel = "Force removal";
        public const string DropRemovalLabel = "Drop removal";
        public const string StartRemovalLabel = "Start removal";
        public const string StartVoteLabel = "Start the vote";
        public const string ForceNowLabel = "Force: delete now";
        public const string ConfirmDeleteLabel = "Yes, delete now";
        public const string RemoveTitleLabel = "Remove a title";
        public const string WholeShowOption = "The whole show";
        public const string RemovalScopePlaceholder = "What to remove";
        public const string RemovalPickPlaceholder = "Pick a title";
        public const string RemoveSearchTitle = "Remove a title";
        public const string RemoveSearchField = "Title in Sonarr or Radarr";

        public const string RemovalNotInLibrary = "That title isn't in Sonarr or Radarr any more.";
        public const string RemovalOnlyYours = "You can only ask to remove titles you requested.";
        public const string RemovalNotYours = "That isn't yours to answer.";
        public const string RemovalNotFound = "That removal doesn't exist any more.";
        public const string RemovalNoSuchSeason = "That show doesn't have those seasons.";
        public const string RemovalInstanceGone = "its Sonarr or Radarr is no longer set up";
        public const string RemovalKeepNoted = "Noted: you want to keep it. One keep is enough; you can take it back while the vote is open.";
        public const string RemovalKeepTakenBack = "Your keep is taken back.";
        public const string RemovalNothingFound = "Nothing in Sonarr or Radarr matches that.";
        public const string RemovalNothingDownloaded = "Nothing of this is downloaded yet, so there is nothing to remove.";

        public static string RemovalScope(Removals.RemovalProposal proposal) =>
            proposal.WholeTitle ? $"**{proposal.Label}**" : $"**{proposal.Label}**, {(proposal.Seasons.Count == 1 ? "season" : "seasons")} {Ranges(proposal.Seasons)}";

        public static string RemovalScope(string label, IReadOnlyCollection<int> seasons) =>
            seasons == null || seasons.Count == 0 ? $"**{label}**" : $"**{label}**, {(seasons.Count == 1 ? "season" : "seasons")} {Ranges(seasons)}";

        private static string Closing(DateTime utc) => $"<t:{new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds()}:f>";

        // The "still watching?" prompt, in each voter's own channel.
        public static string RemovalPrompt(Removals.RemovalProposal proposal, DateTime closes, bool flagged) =>
            $"🗳️ **Still watching?** · {RemovalScope(proposal)} may be removed from the library. " +
            (flagged
                ? $"You asked to keep it, so it stays unless the admin decides otherwise. You can take that back until {Closing(closes)}."
                : $"Press **{RemovalKeepLabel}** if you still watch it: one keep is enough. Voting closes {Closing(closes)}; nothing changes until then.");

        // The requester's own say, first, when an admin starts it.
        public static string RemovalAskRequester(Removals.RemovalProposal proposal, DateTime answerBy, DateTime? reminder) =>
            $"🗑️ **Remove your request?** · The admin would like to remove {RemovalScope(proposal)}, which you requested. " +
            $"**{RemovalYesLabel}** puts it to a vote of everyone who can watch it; **{RemovalNoLabel}** keeps it. " +
            $"No answer by {Closing(answerBy)} counts as yes{(reminder.HasValue ? $" (you'll get a reminder {Closing(reminder.Value)})" : string.Empty)}.";

        public static string RemovalReminder(Removals.RemovalProposal proposal, DateTime answerBy) =>
            $"⏰ **Reminder** · {RemovalAskRequester(proposal, answerBy, null).Replace("🗑️ **Remove your request?** · ", string.Empty)}";

        public static string RemovalYouSaidYes(Removals.RemovalProposal proposal, DateTime closes) =>
            $"🗳️ **Up for a vote** · You agreed to remove {RemovalScope(proposal)}. Everyone who can watch it has until {Closing(closes)} to keep it.";

        public static string RemovalYouSaidNo(Removals.RemovalProposal proposal) => $"✅ **Kept** · You chose to keep {RemovalScope(proposal)}. The admin has been told.";

        public static string RemovalNoAnswer(Removals.RemovalProposal proposal, DateTime closes) =>
            $"🗳️ **Up for a vote** · No answer came, so the removal of {RemovalScope(proposal)} went to a vote, as the message said. You can still press **{RemovalKeepLabel}** on the vote until {Closing(closes)}.";

        public static string RemovalStartedByYou(Removals.RemovalProposal proposal, DateTime closes) =>
            $"🗳️ **Up for a vote** · You asked to remove {RemovalScope(proposal)}. Everyone who can watch it has until {Closing(closes)} to keep it; you'll see the outcome here.";

        // The outcome, reposted for everyone who was asked, without buttons. Flaggers are never named.
        public static string RemovalOutcome(Removals.RemovalProposal proposal) => proposal.Status switch
        {
            Removals.RemovalStatus.Deleted => $"🗑️ **Removed** · {RemovalScope(proposal)} was removed from the library: nobody asked to keep it.{AgainNote(proposal)}",
            Removals.RemovalStatus.Forced => $"🗑️ **Removed by the admin** · {RemovalScope(proposal)} was removed from the library.{AgainNote(proposal)}",
            Removals.RemovalStatus.Flagged or Removals.RemovalStatus.Kept => $"✅ **Kept** · {RemovalScope(proposal)} stays: someone still watches it.",
            _ => $"✅ **Kept** · The removal of {RemovalScope(proposal)} was called off."
        };

        private static string AgainNote(Removals.RemovalProposal proposal) => proposal.WholeTitle ? " It can be requested again any time." : " The rest of the show stays.";

        // On the request message after a removal.
        public static string RemovalNote(Removals.RemovalProposal proposal) =>
            proposal.WholeTitle
                ? $"🗑️ Removed from the library{(proposal.Status == Removals.RemovalStatus.Forced ? " by the admin" : " after a vote")}."
                : $"🗑️ {(proposal.Seasons.Count == 1 ? "Season" : "Seasons")} {Ranges(proposal.Seasons)} removed{(proposal.Status == Removals.RemovalStatus.Forced ? " by the admin" : " after a vote")}.";

        public const string FinalRemoved = "🗑️ **Removed**";

        // Dev stack only (docker/dev.ps1 fast-votes): posted in the admin channel when it changes.
        public static string FastVotesOn(TimeSpan vote, TimeSpan timeout, TimeSpan reminder, int voteDays, int timeoutDays, int reminderDay) =>
            $"🧪 **Dev: fast votes are on.** Removal votes now close after {Duration(vote)}, and requesters have {Duration(timeout)} to answer (reminded after {Duration(reminder)}). " +
            $"These are test timings for this local test stack, not the real settings ({voteDays} days, {timeoutDays} days, reminder on day {reminderDay}). Removals already open keep their times. `.\\docker\\dev.ps1 fast-votes off` turns it off.";

        public static string FastVotesOff(int voteDays, int timeoutDays, int reminderDay) =>
            $"🧪 **Dev: fast votes are off.** Removal timings are back to the real settings: votes {voteDays} days, requesters {timeoutDays} days, reminder on day {reminderDay}.";

        public static string RequestRemovedStatus(Request request) => $"Request #{request.Id} for {request.Label} was removed from the library. You can request it again any time.";

        // For the admin.
        public static string RemovalVoteStarted(Removals.RemovalProposal proposal) =>
            $"Started: everyone with {Label(proposal.ContentType)} access has until {Closing(proposal.WindowClosesAt ?? DateTime.UtcNow)} to keep {RemovalScope(proposal)}.";

        public static string RemovalAskedRequester(Removals.RemovalProposal proposal, string requester, DateTime answerBy) =>
            $"Asked {requester} first, in their channel. No answer by {Closing(answerBy)} counts as yes and starts the vote.";

        public static string RemovalAlreadyOpen(Removals.RemovalProposal open) =>
            $"There's already a removal open for {RemovalScope(open)} ({Label(open.Status)}). Finish or drop that one first; other seasons can be removed alongside it.";

        public static string RemovalAlreadyMoved(Removals.RemovalProposal proposal) => $"That removal has moved on: it's {Label(proposal.Status)} now.";

        public static string RemovalVoteClosed(Removals.RemovalProposal proposal) => $"The vote on {RemovalScope(proposal)} has closed.";

        public static string RemovalDropped(Removals.RemovalProposal proposal) => $"Dropped: {RemovalScope(proposal)} stays.";

        public static string RemovalDone(Removals.RemovalProposal proposal, string instance) =>
            proposal.WholeTitle
                ? $"Removed {RemovalScope(proposal)} from {instance} with its files. It wasn't added to the exclusion list, so it can be requested again."
                : $"Removed {RemovalScope(proposal)} from {instance}: those seasons' files are deleted and the seasons unmonitored. The show stays.";

        public static string RemovalFailed(Removals.RemovalProposal proposal, string instance) =>
            $"{instance ?? "Sonarr or Radarr"} refused to remove {RemovalScope(proposal)}: {proposal.Detail}. It's waiting in `/admin requests` (Force tries again).";

        public static string Label(Removals.RemovalStatus status) => status switch
        {
            Removals.RemovalStatus.AwaitingRequester => "waiting on the requester",
            Removals.RemovalStatus.Voting => "voting",
            Removals.RemovalStatus.Declined => "the requester said keep",
            Removals.RemovalStatus.Flagged => "kept by a vote",
            Removals.RemovalStatus.Failed => "failed",
            Removals.RemovalStatus.Forced => "removed by the admin",
            Removals.RemovalStatus.Deleted => "removed by vote",
            Removals.RemovalStatus.Kept => "kept",
            _ => "cancelled"
        };

        // The feed card's removal line, at every step.
        public static string FeedRemovalLine(Removals.RemovalProposal proposal, int keeps, string requester) =>
            FeedRemovalState(proposal, keeps, requester) + (proposal.WholeTitle ? string.Empty : $" · {(proposal.Seasons.Count == 1 ? "season" : "seasons")} {Ranges(proposal.Seasons)}");

        private static string FeedRemovalState(Removals.RemovalProposal proposal, int keeps, string requester) => proposal.Status switch
        {
            Removals.RemovalStatus.AwaitingRequester => $"🗑️ Removal: asked {requester ?? "the requester"}, answer by {Closing(proposal.AnswerBy ?? DateTime.UtcNow)}",
            Removals.RemovalStatus.Voting => $"🗑️ Removal vote until {Closing(proposal.WindowClosesAt ?? DateTime.UtcNow)} · {keeps} keep",
            Removals.RemovalStatus.Declined => $"🗑️ Removal: {requester ?? "the requester"} said keep (Force or drop)",
            Removals.RemovalStatus.Flagged => $"🗑️ Removal: kept by {keeps} keep (Force or drop)",
            Removals.RemovalStatus.Kept => $"🗑️ Removal: kept ({keeps} keep)",
            Removals.RemovalStatus.Forced => $"🗑️ Removed by {proposal.ClosedByName ?? "the admin"} (forced)",
            Removals.RemovalStatus.Deleted => "🗑️ Removed by vote",
            Removals.RemovalStatus.Failed => $"🗑️ Removal failed: {proposal.Detail}",
            _ => $"🗑️ Removal cancelled{(proposal.ClosedByName != null ? $" by {proposal.ClosedByName}" : string.Empty)}"
        };

        public static string RemovalFeedMeta(Removals.RemovalProposal proposal, string instance) =>
            $"{(proposal.ContentType == ContentType.Tv ? "📺 TV" : "🎬 Movie")}{Dot}not requested through Requestarr{(instance != null ? Dot + instance : string.Empty)}";

        public static string RemovalFeedFooter(Removals.RemovalProposal proposal) => $"Removal #{proposal.Id}";

        // The admin's removal card (from the feed's Delete or the panel's Remove a title).
        public static string RemovalStartCard(Removals.RemovalTarget target, IReadOnlyCollection<int> seasons) =>
            $"**Remove {RemovalScope(target.Label, seasons)}** · {target.Instance.Name}\n" +
            (target.Requester != null
                ? $"Requested by **{target.Requester.DisplayName}**: they're asked first, and no answer in the requester window counts as yes."
                : "Not requested through Requestarr (or its requester was removed): it goes straight to the vote.") +
            "\nEveryone with access gets a **Keep it** button; nothing is deleted until the vote closes with no keeps. **Force** deletes now.";

        public static string RemovalUserCard(Request request, IReadOnlyCollection<int> seasons) =>
            $"**Remove {RemovalScope(request.Label, seasons)}?** Everyone who can watch it gets to keep it, and nothing changes until the vote closes. If nobody keeps it, it's removed from the library and its files are deleted.";

        public static string RemovalConfirmForce(string label, IReadOnlyCollection<int> seasons) =>
            $"Delete {RemovalScope(label, seasons)} **now**? Its files are deleted; this can't be undone. Nobody is asked.";

        public static string RemovalSearchResults(string term, int count) => $"**Remove a title** · {count} match{(count == 1 ? string.Empty : "es")} for \"{term}\". Pick one.";

        public static string RemovalChoice(Removals.RemovalTarget target) =>
            DiscordLimits.Truncate($"{target.Label} · {target.Instance.Name} · {(target.Requester != null ? $"requested by {target.Requester.DisplayName}" : "not requested")}", DiscordLimits.OptionLabelLength);

        // In /admin requests.
        public static string RemovalWaiting(Removals.RemovalProposal proposal, string requester, IReadOnlyCollection<string> flaggers) => proposal.Status switch
        {
            Removals.RemovalStatus.AwaitingRequester => $"🗑️ The removal of {RemovalScope(proposal)} is waiting on **{requester}**, asked {DiscordTimestamp(proposal.Created)}. No answer by {Closing(proposal.AnswerBy ?? DateTime.UtcNow)} counts as yes and starts the vote. **{ForceRemovalLabel}** skips both.",
            Removals.RemovalStatus.Declined => $"🗑️ **{requester}** said keep {RemovalScope(proposal)}. **{ForceRemovalLabel}** removes it anyway; **{DropRemovalLabel}** leaves it.",
            Removals.RemovalStatus.Flagged => $"🗑️ The vote kept {RemovalScope(proposal)}: {flaggers.Count} keep ({string.Join(", ", flaggers)}). **{ForceRemovalLabel}** removes it anyway; **{DropRemovalLabel}** leaves it.",
            _ => $"🗑️ Removing {RemovalScope(proposal)} failed: {proposal.Detail}. **{ForceRemovalLabel}** tries again; **{DropRemovalLabel}** leaves it."
        };

        // The admin console: /admin user and /admin request cards, the member picker, queue and users views.
        public const string PanelMembers = "Add or find a member";
        public const string PickMemberPrompt = "Pick a member to add them or open their card.";
        public const string PickMemberPlaceholder = "Pick a member";
        public const string AddMemberLabel = "Add to Requestarr";
        public const string AdminLinkPlexLabel = "Link Plex…";
        public const string ChangePlexLabel = "Change Plex link…";
        public const string SuspendLabel = "Suspend";
        public const string UnsuspendLabel = "Unsuspend";
        public const string RemoveLabel = "Remove";
        public const string ConfirmRemoveLabel = "Yes, remove";
        public const string KeepLabel = "Keep";
        public const string TheirRequestsLabel = "Their requests";
        public const string ApproveLinkLabel = "Approve Plex link";
        public const string DenyLinkLabel = "Deny Plex link";
        public const string LimitTitle = "Request limit";
        public const string LimitField = "Requests allowed (0 = no limit)";
        public const string DaysField = "Per how many days";
        public const string PlexTitle = "Link a Plex account";
        public const string PlexField = "Plex username or email";
        public const string PickRequestPlaceholder = "Open a request";
        public const string AllTypesLabel = "All";
        public const string NoRequestMatches = "No request matches that. Use its number (for example `12`) or part of its title.";
        public const string CancelReasonTitle = "Cancel request";
        public const string CancelReasonLabel = "Reason shown to the user (optional)";
        public const string InvalidLimit = "The limit must be a number from 0 to 1000, and the days from 1 to 365.";

        public static string MemberNotAdded(string name, ulong discordId) =>
            $"**{name}** (<@{discordId}>) isn't a Requestarr user. **{AddMemberLabel}** creates their role, private channel and panel; Movies and TV start as needs approval, Music off.";

        public static string ConfirmRemove(string name) =>
            $"Remove **{name}**? Their channel is archived and their role deleted; their request history is kept.";

        public static string PendingLink(MediaServers.MediaServerProvider provider, string username) => $"Waiting for you: a {provider} link request as `{username}`.";

        public static string AccessOption(ContentType type, AccessState state) => $"{Label(type)}: {Capitalize(Label(state))}";

        public static string LimitOption(ContentType type, UserMediaAccess access) =>
            $"{Label(type)} limit (now {(access?.QuotaLimit is { } limit ? $"{limit} per {(int)(access.QuotaWindow ?? TimeSpan.FromDays(7)).TotalDays} days" : "none")})";

        public static string InstanceOption(ContentType type, string instance) => $"{Label(type)}: {instance}";

        public static string Current => "current";

        public static string InstanceLine(ContentType type, string instance) => $"• {Label(type)} requests go to {instance}";

        public static string MemberRequestsHeader(string name, int count) => $"**{name}'s requests** · {count}";

        public static string RequestMatches(string query, int count) => $"**{count} requests match \"{query}\"**. Pick one to open it.";

        public static string UsersHeader(int count) => $"**Users** · {count}";

        public static string RequestChoice(Request request, string owner) =>
            DiscordLimits.Truncate($"#{request.Id} {request.Label} · {Label(request.Status)} · {owner}", DiscordLimits.OptionLabelLength);

        // Connection states: what the admins are told, and what users see while an instance is down.
        public static ContentType TypeOf(Backends.BackendInstance instance) =>
            instance.Implementation == Backends.BackendImplementations.Sonarr ? ContentType.Tv
            : instance.Implementation == Backends.BackendImplementations.Lidarr ? ContentType.Music
            : ContentType.Movie;

        public static string InstanceDownAlert(Backends.BackendInstance instance) =>
            $"⚠️ **{instance.Name} can't be reached** since <t:{new DateTimeOffset(DateTime.SpecifyKind(instance.DownSince ?? DateTime.UtcNow, DateTimeKind.Utc)).ToUnixTimeSeconds()}:f> ({Label(TypeOf(instance))}). " +
            $"Users still see {Label(TypeOf(instance))}, but new {Label(TypeOf(instance))} requests tell them requests are paused; nobody's access changes. Requestarr checks again every minute and says here when it's back.";

        public static string InstanceBackAlert(Backends.BackendInstance instance, TimeSpan downFor) =>
            $"✅ **{instance.Name} is back** after {Duration(downFor)}. {Label(TypeOf(instance))} requests work again.";

        public static string Duration(TimeSpan span) =>
            span.TotalMinutes < 90 ? Minutes(Math.Max(1, (int)Math.Round(span.TotalMinutes)))
            : span.TotalHours < 48 ? $"{span.TotalHours:0.#} hours"
            : $"{span.TotalDays:0.#} days";

        private static string Minutes(int n) => $"{n} minute{(n == 1 ? string.Empty : "s")}";

        public static string RequestsPaused(ContentType type, string instance) =>
            $"{Label(type)} requests are paused right now: {instance} can't be reached. Requestarr keeps trying, and the admin is alerted if it stays down.";

        public static string TypeNotAvailable(ContentType type) => $"{Label(type)} can't be requested here yet.";

        public static string NotConnectedYet(string name, ContentType type) =>
            $"Saved. {name} won't see {Label(type)} until a {Backends.BackendImplementations.ForContentType(type)} instance is connected in Requestarr.";

        // Outages announced to users: plain words, what's affected, and that the admin knows.
        public const string AnnounceLabel = "Announce to users";

        public static string AnnounceOffer(TimeSpan after) =>
            $"Still down after {Duration(after)}. **{AnnounceLabel}** posts a short notice in #{ChannelLayout.AnnouncementsChannelName}; if you do, the recovery is announced there too.";

        public static string AlertAnnounced(string admin) => $"📣 Announced to users by {admin}. The recovery will be announced there too.";

        public static string AlertNowBack(TimeSpan downFor, bool announced) =>
            $"✅ Back after {Duration(downFor)}.{(announced ? " The recovery was announced to users." : string.Empty)}";

        public static string AnnounceNotDown(string instance) =>
            instance == null ? "That instance no longer exists." : $"{instance} is back already, so there's nothing to announce.";

        public static string AlreadyAnnounced(string instance) => $"This {instance} outage was already announced to users.";

        public const string AnnounceFailed = "Couldn't post in the announcements channel. Check that it exists and that the bot can post there.";

        public static string Announced(string instance) =>
            $"Posted in #{ChannelLayout.AnnouncementsChannelName}. When {instance} is back, the recovery is announced there too.";

        public static string OutageNotice(ContentType type, bool partly) =>
            $"⚠️ **{(partly ? "Some " : string.Empty)}{Label(type)} requests are paused**\n" +
            $"Requestarr can't pass {(partly ? "some " : string.Empty)}new {Label(type)} requests on right now. The admin knows and is working on it. " +
            "Everything you already asked for is safe, and there'll be a message here when it works again.";

        public static string OutageOverNotice(ContentType type, TimeSpan downFor) =>
            $"✅ **{Label(type)} requests work again**\nEverything is back to normal after {Duration(downFor)}. Thanks for your patience.";

        // What's new on upgrade: the full list for admins; only the "For users" lines, as written, for members.
        // What's new in one version. Only sections with something in them are shown; a version with nothing for
        // users or admins isn't posted at all. Each post starts with its header, so a server can find its posts again.
        public static string VersionNotesAdminHeader(Version version) => $"📋 **Requestarr {version}: what's new**";

        public static string VersionNotesUsersHeader(Version version) => $"✨ **What's new in Requestarr {version}**";

        public const string VersionNotesContinued = " (continued)";

        public static string VersionNotesForAdmins(Announcements.ChangelogVersion version, bool postedToUsers)
        {
            var lines = new List<string> { VersionNotesAdminHeader(version.Version) };

            if (version.Users.Count > 0)
            {
                lines.Add($"**{Announcements.Changelog.UsersHeading}**{(postedToUsers ? $" (posted in #{ChannelLayout.AnnouncementsChannelName})" : string.Empty)}");
                lines.AddRange(version.Users.Select(l => $"• {l}"));
            }

            if (version.Admins.Count > 0)
            {
                lines.Add($"**{Announcements.Changelog.AdminsHeading}**");
                lines.AddRange(version.Admins.Select(l => $"• {l}"));
            }

            return string.Join("\n", lines);
        }

        public static string VersionNotesForUsers(Announcements.ChangelogVersion version) =>
            string.Join("\n", version.Users.Select(l => $"• {l}").Prepend(VersionNotesUsersHeader(version.Version)));

        // /admin requests: one thing waiting on the admin at a time, oldest first.
        public const string WalkDone = "Nothing left waiting on you. 🎉";
        public const string AlreadyHandled = "That one was already handled; here's the next.";
        public const string SkipLabel = "Skip";
        public const string StartOverLabel = "Start over";
        public const string SearchAgainLabel = "Search again";
        public const string RetrySendLabel = "Try adding again";
        public const string CheckNowLabel = "Check now";
        public const string Requester = "asked";
        public const string Follower = "follows";

        public static string WalkHeader(int position, int total) => $"**Waiting on you** · {position} of {total}";

        public static string WalkEnd(int left) =>
            $"That's the end of the list. {left} skipped item{(left == 1 ? string.Empty : "s")} still wait{(left == 1 ? "s" : string.Empty)}; **{StartOverLabel}** goes through them again.";

        public static string PanelPendingQueueCount(int count) => count > 0 ? $"{PanelPendingQueue} ({count})" : PanelPendingQueue;

        public static string WaitingSince(Request request) =>
            $"Request #{request.Id} for {request.Label} is waiting for approval since <t:{Unix(request.Requested)}:R>.";

        // What went wrong with an approved request, in plain words, and what the buttons do.
        public static string TroubleText(Request request, string instance) => request.Status == RequestStatus.Failed
            ? $"Request #{request.Id} for {request.Label} couldn't be added to {instance}{Because(request.DecisionReason)}. **{RetrySendLabel}** once that's fixed; **{CancelLabel}** drops it; **{AlreadyAvailableLabel}** closes it if you have the title another way."
            : request.Progress == DownloadProgress.Failed
                ? $"The download for request #{request.Id} ({request.Label}) failed{Because(request.ProgressDetail)}. {instance} usually blocklists that release and tries another by itself. **{SearchAgainLabel}** asks it to look now; **{CancelLabel}** stops the search (nothing is deleted); **{AlreadyAvailableLabel}** closes it if you have the title another way."
                : $"The download for request #{request.Id} ({request.Label}) is stuck{Because(request.ProgressDetail)}. **{SearchAgainLabel}** asks {instance} for another release (the stuck download stays in your download client until you remove it there); **{CancelLabel}** stops the search (nothing is deleted); **{AlreadyAvailableLabel}** closes it if you have the title another way.";

        public static string PlexLinkWaiting(string name, string discordId, MediaServers.MediaServerProvider provider, string username, DateTime since) =>
            $"**{name}** (<@{discordId}>) asks for {provider} access as `{username}`, since <t:{Unix(since)}:R>.";

        public static string InstanceWaiting(Backends.BackendInstance instance) =>
            InstanceDownAlert(instance) + $"\nNothing to decide: this stays here until it's back. **{CheckNowLabel}** tries it again now.";

        public static string PersonLine(string name, string role, ContentType type, AccessState state, int used, int? limit, int days, int pending) =>
            $"• **{name}** ({role}): {Label(type)} {Label(state)} · {used}{(limit.HasValue ? $" of {limit}" : string.Empty)} {Label(type)} request{(used == 1 && !limit.HasValue ? string.Empty : "s")} in {days} days{(limit.HasValue ? string.Empty : " (no limit)")} · {pending} waiting for approval";

        public static string NothingToSearch(Request request) => $"Request #{request.Id} isn't in Sonarr or Radarr, so there's nothing to search for.";

        public static string NothingToRetry(Request request) => $"Request #{request.Id} isn't waiting to be added again; it's {Label(request.Status)}.";

        public static string SearchingAgain(Request request, string instance) => $"Asked {instance} to search for {request.Label} (request #{request.Id}) again.";

        public static string AccessSelectPlaceholder(ContentType type) => $"{Label(type)} access";

        public static string LevelLabel(AccessState state) => Capitalize(Label(state));

        public const string MorePlaceholder = "Limits, instance, their requests…";

        public static string InstanceBackNow(string instance) => $"{instance} answers again.";

        public static string StillDown(string instance) => $"{instance ?? "That instance"} still can't be reached.";

        private static long Unix(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();

        public static class Commands
        {
            public const string Admin = "Requestarr admin commands";
            public const string AdminUser = "Open a member's card: add, access, limits, Plex, suspend, remove";
            public const string AdminRequests = "Go through everything waiting on you, one at a time";
            public const string OptionRequestSearch = "Request number or part of its title";
            public const string AdminMediaServer = "Media server access requests";
            public const string RequestOne = "Request a movie or TV show";
            public const string Help = "How to use Requestarr";
            public const string AdminCancel = "Cancel a pending request (admin)";
            public const string AdminAvailable = "Close a pending request as already available (admin)";
            public const string OptionCancelReason = "Reason shown to the user";
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
