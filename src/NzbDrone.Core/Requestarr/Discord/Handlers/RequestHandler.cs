using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Metadata;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // /request {type} {query}: users in their own channel, or the admin in the admin channel.
    // A request is one message that changes in place: results, details, then the final status. For TV,
    // "All seasons" sends at once; "Choose seasons" adds a menu and a plain-words confirmation. Buttons on cards from earlier versions keep working.
    public class RequestHandler : IInteractionHandler
    {
        public const int MaxListLines = 20;

        private readonly ICallerResolver _callers;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;
        private readonly IUserMediaAccessRepository _access;
        private readonly IBackendService _backends;
        private readonly IMetadataService _metadata;

        public RequestHandler(ICallerResolver callers, IRequestService requests, IUserManagementService users, IUserMediaAccessRepository access, IBackendService backends, IMetadataService metadata)
        {
            _callers = callers;
            _requests = requests;
            _users = users;
            _access = access;
            _backends = backends;
            _metadata = metadata;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "request"),
            (InteractionKind.Autocomplete, "request"),
            (InteractionKind.SelectMenu, RequestRoutes.Pick),
            (InteractionKind.SelectMenu, RequestRoutes.TvPick),
            (InteractionKind.SelectMenu, RequestRoutes.Seasons),
            (InteractionKind.Button, RequestRoutes.Results),
            (InteractionKind.Button, RequestRoutes.Make),
            (InteractionKind.Button, RequestRoutes.Follow),
            (InteractionKind.Button, RequestRoutes.TvAll),
            (InteractionKind.Button, RequestRoutes.TvChoose),
            (InteractionKind.Button, RequestRoutes.TvPage),
            (InteractionKind.Button, RequestRoutes.TvReview),
            (InteractionKind.Button, RequestRoutes.TvConfirm),
            (InteractionKind.Button, RequestRoutes.TvBack),
            (InteractionKind.Button, RequestRoutes.Cancel)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var context = _callers.Resolve(interaction);
            var asAdmin = context.RightServer && context.IsAdmin && context.InAdminChannel;

            if (interaction.Kind == InteractionKind.Autocomplete)
            {
                await responder.SuggestAsync(asAdmin || context.UserDenial == null ? TypeChoices(context, asAdmin, interaction.GetOption<string>("type")) : Array.Empty<InteractionChoice>());
                return;
            }

            if (!asAdmin && context.UserDenial != null)
            {
                await responder.Ephemeral(context.UserDenial);
                return;
            }

            var user = asAdmin ? _users.EnsureAdminUser(interaction.UserId, interaction.Username, interaction.DisplayName) : context.User;
            var caller = new RequestCaller(user, asAdmin, HandlerSupport.ActorFor(interaction, context) with { UserId = user.Id }, user.DisplayName);
            var args = interaction.CustomIdArgs;

            switch (interaction.Kind, interaction.Route)
            {
                case (InteractionKind.SlashCommand, _):
                    await HandleRequestCommand(interaction, responder, caller);
                    break;

                case (InteractionKind.SelectMenu, RequestRoutes.Pick):
                {
                    var type = RequestCards.ParseTypeKey(First(args));

                    if (type == null || !int.TryParse(First(interaction.Values), out var id))
                    {
                        await responder.Ephemeral(DiscordText.TitleNotFound);
                        return;
                    }

                    await responder.DeferAsync();
                    await ShowDetail(responder, caller, type.Value, id, args.Count > 1 ? string.Join(":", args.Skip(1)) : null);
                    break;
                }

                // Back to results on an old search just searches again: nothing is changed.
                case (InteractionKind.Button, RequestRoutes.Results):
                {
                    var type = RequestCards.ParseTypeKey(First(args));
                    var query = args.Count > 1 ? string.Join(":", args.Skip(1)) : null;

                    if (type == null || string.IsNullOrWhiteSpace(query))
                    {
                        await responder.Ephemeral(DiscordText.TitleNotFound);
                        return;
                    }

                    await responder.DeferAsync();
                    await ShowResults(responder, caller, type.Value, query);
                    break;
                }

                // All seasons sends straight away; the final status says exactly what was requested.
                case (InteractionKind.Button, RequestRoutes.TvAll):
                    await RequestTv(interaction, responder, caller, SeasonChoice.Everything);
                    break;

                case (InteractionKind.Button, RequestRoutes.TvConfirm):
                    await RequestTv(interaction, responder, caller, args.Count > 1 ? RequestCards.DecodeSeasons(args[1]) : null);
                    break;

                // The single season menu on cards from before the two-path flow.
                case (InteractionKind.SelectMenu, RequestRoutes.Seasons):
                    await RequestTv(interaction, responder, caller, RequestCards.ParseSeasons(interaction.Values));
                    break;

                case (InteractionKind.Button, RequestRoutes.TvChoose):
                case (InteractionKind.Button, RequestRoutes.TvBack):
                case (InteractionKind.Button, RequestRoutes.TvPage):
                case (InteractionKind.Button, RequestRoutes.TvReview):
                case (InteractionKind.SelectMenu, RequestRoutes.TvPick):
                    await HandleSeasonStep(interaction, responder, caller);
                    break;

                case (InteractionKind.Button, RequestRoutes.Cancel):
                    await HandleCancel(interaction, responder, caller);
                    break;

                // Follow only ever follows. Old TV cards used req-make for Follow, so it means the same.
                case (InteractionKind.Button, RequestRoutes.Follow):
                case (InteractionKind.Button, RequestRoutes.Make) when RequestCards.ParseTypeKey(First(args)) == ContentType.Tv:
                    await HandleFollow(interaction, responder, caller);
                    break;

                // Request (movies; also Follow on movie cards from earlier versions).
                case (InteractionKind.Button, RequestRoutes.Make):
                {
                    var type = RequestCards.ParseTypeKey(First(args));

                    if (type == null || args.Count < 2 || !int.TryParse(args[1], out var id))
                    {
                        await responder.Ephemeral(DiscordText.TitleNotFound);
                        return;
                    }

                    await responder.DeferAsync();
                    var result = await _requests.RequestAsync(caller, type.Value, id);
                    await Finish(interaction, responder, type.Value, result, SeasonText(result, null));
                    break;
                }
            }
        }

        // Sends a TV request, unless the title was requested or added since the card was shown:
        // then nothing changes and the message shows the title as it is now.
        private async Task RequestTv(DiscordInteraction interaction, IInteractionResponder responder, RequestCaller caller, SeasonChoice choice)
        {
            if (!int.TryParse(First(interaction.CustomIdArgs), out var tvdbId) || choice == null)
            {
                await responder.Ephemeral(choice == null ? DiscordText.ChooseSeasons : DiscordText.TitleNotFound);
                return;
            }

            await responder.DeferAsync();
            var detail = await _requests.DetailAsync(caller, ContentType.Tv, tvdbId);

            if (detail.Error == null && (detail.Value.Item.LibraryId.HasValue || detail.Value.Active != null))
            {
                await responder.EditAsync(RequestCards.Detail(ContentType.Tv, detail.Value));
                await responder.Ephemeral(detail.Value.Item.LibraryId.HasValue ? DiscordText.AlreadyThereNote : DiscordText.NothingChanged(RequestCards.StateLine(detail.Value)));
                return;
            }

            var result = await _requests.RequestAsync(caller, ContentType.Tv, tvdbId, choice);
            await Finish(interaction, responder, ContentType.Tv, result, SeasonText(result, choice));
        }

        private async Task HandleFollow(DiscordInteraction interaction, IInteractionResponder responder, RequestCaller caller)
        {
            var args = interaction.CustomIdArgs;
            var type = RequestCards.ParseTypeKey(First(args));

            if (type == null || args.Count < 2 || !int.TryParse(args[1], out var id))
            {
                await responder.Ephemeral(DiscordText.TitleNotFound);
                return;
            }

            await responder.DeferAsync();
            var detail = await _requests.DetailAsync(caller, type.Value, id);

            if (detail.Error != null)
            {
                await responder.EditAsync(RequestCards.Stopped(type.Value, detail.Error));
                return;
            }

            var state = detail.Value;

            // Nothing active to follow any more (denied, cancelled, added), or already yours.
            if (state.Active == null || state.CallerOwnsActive || state.CallerFollows || state.Item.LibraryId.HasValue)
            {
                await responder.EditAsync(RequestCards.Detail(type.Value, state));
                await responder.Ephemeral(state.Item.LibraryId.HasValue ? DiscordText.AlreadyThereNote
                    : state.Active == null ? DiscordText.NothingToFollow
                    : DiscordText.NothingChanged(RequestCards.StateLine(state)));
                return;
            }

            var result = await _requests.RequestAsync(caller, type.Value, id);
            await responder.EditAsync(RequestCards.Final(type.Value, result));
        }

        // Cancel re-checks on the server: only a pending request of the caller's is cancelled.
        // Late clicks change nothing; they get the current state privately and the message is
        // refreshed so its stale button goes away.
        private async Task HandleCancel(DiscordInteraction interaction, IInteractionResponder responder, RequestCaller caller)
        {
            var request = int.TryParse(First(interaction.CustomIdArgs), out var id) ? _requests.Find(id) : null;

            if (request == null)
            {
                await responder.Ephemeral(DiscordText.RequestNotFound);
                return;
            }

            var result = await _requests.CancelAsync(caller, id);
            var current = _requests.Find(id);

            if (!result.Success && current.OwnerUserId != caller.User.Id)
            {
                await responder.Ephemeral(result.Message);
                return;
            }

            await responder.UpdateMessageAsync(await _requests.StatusReplyAsync(current));

            if (!result.Success)
            {
                await responder.Ephemeral(DiscordText.NothingChanged(DiscordText.RequestState(current)));
            }
        }

        // The end of a request: the status, and for a pending request the message it lives in,
        // so it can be edited when an admin decides.
        private async Task Finish(DiscordInteraction interaction, IInteractionResponder responder, ContentType type, RequestResult result, string seasonsText)
        {
            await responder.EditAsync(RequestCards.Final(type, result, seasonsText));

            if (result.Outcome == RequestOutcome.Pending && result.Request != null && await responder.MessageIdAsync() is { } messageId)
            {
                _requests.AttachMessage(result.Request.Id, interaction.ChannelId, messageId);
            }
        }

        private async Task HandleSeasonStep(DiscordInteraction interaction, IInteractionResponder responder, RequestCaller caller)
        {
            var args = interaction.CustomIdArgs;

            if (!int.TryParse(First(args), out var tvdbId))
            {
                await responder.Ephemeral(DiscordText.TitleNotFound);
                return;
            }

            await responder.DeferAsync();
            var detail = await _requests.DetailAsync(caller, ContentType.Tv, tvdbId);

            if (detail.Error != null)
            {
                await responder.EditAsync(RequestCards.Stopped(ContentType.Tv, detail.Error));
                return;
            }

            var state = detail.Value;

            // Already requested or in the library since the card was shown: show the current state.
            if (state.Item.LibraryId.HasValue || state.Active != null || interaction.Route == RequestRoutes.TvBack)
            {
                await responder.EditAsync(RequestCards.Detail(ContentType.Tv, state));
                return;
            }

            // Pages and menus carry {page}:{picks}; Continue carries just {picks}.
            var review = interaction.Route == RequestRoutes.TvReview;
            var page = !review && args.Count > 1 && int.TryParse(args[1], out var p) ? p : 0;
            var picks = review ? (args.Count > 1 ? RequestCards.DecodeSeasons(args[1]) : null) : args.Count > 2 ? RequestCards.DecodeSeasons(args[2]) : null;

            var reply = interaction.Route switch
            {
                RequestRoutes.TvChoose => RequestCards.SeasonPicker(state, 0, null),
                RequestRoutes.TvPage => RequestCards.SeasonPicker(state, page, picks),
                RequestRoutes.TvReview when picks != null => RequestCards.Confirm(state, picks),
                RequestRoutes.TvReview => RequestCards.SeasonPicker(state, 0, null),

                // The menu on a page: those picks replace that page's, the rest are kept.
                _ when args.Count > 1 => RequestCards.SeasonPicker(state, page, RequestCards.WithPagePicks(picks, RequestCards.PageSeasons(state.Item, page), interaction.Values)),

                // The single-page menu from before pages: straight to the confirmation.
                _ => RequestCards.Confirm(state, RequestCards.FromMenu(interaction.Values))
            };

            await responder.EditAsync(reply);
        }

        private async Task HandleRequestCommand(DiscordInteraction interaction, IInteractionResponder responder, RequestCaller caller)
        {
            var type = ParseRequestType(interaction.GetOption<string>("type"));
            var query = interaction.GetOption<string>("query")?.Trim();

            if (type == null)
            {
                await responder.Ephemeral(DiscordText.ChooseType);
                return;
            }

            if (type == ContentType.Music)
            {
                await responder.Ephemeral(DiscordText.MusicNotYet);
                return;
            }

            if (!caller.AsAdmin && AccessState(caller.User, type.Value) == Users.AccessState.Off)
            {
                await responder.Ephemeral(DiscordText.AccessOff(type.Value));
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                await responder.Ephemeral(DiscordText.NothingFound);
                return;
            }

            var (idType, id) = ParseIdQuery(query);

            if (idType != null)
            {
                if (idType != type)
                {
                    await responder.Ephemeral(idType == ContentType.Movie ? DiscordText.TmdbIsForMovies : DiscordText.TvdbIsForTv);
                    return;
                }

                await responder.DeferAsync();
                await ShowDetail(responder, caller, type.Value, id, null);
                return;
            }

            await responder.DeferAsync();
            await ShowResults(responder, caller, type.Value, query);
        }

        private async Task ShowResults(IInteractionResponder responder, RequestCaller caller, ContentType type, string query)
        {
            var results = await _requests.SearchAsync(caller, type, query);

            if (results.Error != null)
            {
                await responder.EditAsync(RequestCards.Stopped(type, results.Error));
                return;
            }

            // One match: skip the menu and go straight to its card.
            if (results.Value.Count == 1)
            {
                await ShowDetail(responder, caller, type, results.Value[0].ExternalId(type).Value, null);
                return;
            }

            await responder.EditAsync(RequestCards.SearchResults(type, query, results.Value, _metadata.CastEnabled));
        }

        // Only types the caller can actually request: access not off (admins: types with a backend).
        // Music waits for Lidarr; comics and books are never offered yet.
        private IReadOnlyList<InteractionChoice> TypeChoices(CallerContext context, bool asAdmin, string typed)
        {
            var types = ContentTypes.Requestable
                .Where(t => asAdmin ? _backends.Default(t) != null : AccessState(context.User, t) != Users.AccessState.Off);

            return types
                .Select(t => new InteractionChoice(DiscordText.Label(t), RequestCards.TypeKey(t)))
                .Where(c => string.IsNullOrWhiteSpace(typed) || c.Name.Contains(typed.Trim(), StringComparison.OrdinalIgnoreCase) || c.Value.Contains(typed.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private AccessState AccessState(RequestarrUser user, ContentType type)
        {
            return _access.GetForUser(user.Id).FirstOrDefault(a => a.ContentType == type)?.State ?? UserManagementService.DefaultAccess[type];
        }

        public static ContentType? ParseRequestType(string value) => value?.Trim().ToLowerInvariant() switch
        {
            "movie" or "movies" => ContentType.Movie,
            "tv" or "tv shows" or "show" or "shows" => ContentType.Tv,
            "music" => ContentType.Music,
            _ => null
        };

        public static (ContentType? Type, int Id) ParseIdQuery(string query)
        {
            var parts = query.Split(':', 2);

            if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var id) && id > 0)
            {
                switch (parts[0].Trim().ToLowerInvariant())
                {
                    case "tmdb":
                        return (ContentType.Movie, id);
                    case "tvdb":
                        return (ContentType.Tv, id);
                }
            }

            return (null, 0);
        }

        private async Task ShowDetail(IInteractionResponder responder, RequestCaller caller, ContentType type, int externalId, string query)
        {
            var detail = await _requests.DetailAsync(caller, type, externalId);

            if (detail.Error != null)
            {
                await responder.EditAsync(RequestCards.Stopped(type, detail.Error));
                return;
            }

            await responder.EditAsync(RequestCards.Detail(type, detail.Value, query));
        }

        // What was actually requested, in plain words, for the final status.
        private static string SeasonText(RequestResult result, SeasonChoice choice)
        {
            if (result.Request == null || result.Request.ContentType != ContentType.Tv)
            {
                return null;
            }

            return choice is { All: true, Future: true } && result.Item != null
                ? DiscordText.SeasonChoiceText(choice, result.Item.Seasons)
                : DiscordText.Seasons(result.Request);
        }

        private static string First(IReadOnlyList<string> values) => values.Count > 0 ? values[0] : null;
    }
}
