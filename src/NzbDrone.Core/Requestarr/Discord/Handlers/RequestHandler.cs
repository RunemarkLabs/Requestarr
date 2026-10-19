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
    // A request is one message that changes in place: results, details, seasons (TV), a plain-words
    // confirmation, then the final status. Buttons on cards from earlier versions keep working.
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
            (InteractionKind.Button, RequestRoutes.TvAll),
            (InteractionKind.Button, RequestRoutes.TvChoose),
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

                case (InteractionKind.Button, RequestRoutes.TvAll):
                case (InteractionKind.Button, RequestRoutes.TvChoose):
                case (InteractionKind.Button, RequestRoutes.TvBack):
                case (InteractionKind.SelectMenu, RequestRoutes.TvPick):
                    await HandleSeasonStep(interaction, responder, caller);
                    break;

                case (InteractionKind.Button, RequestRoutes.TvConfirm):
                {
                    var choice = args.Count > 1 ? RequestCards.DecodeSeasons(args[1]) : null;

                    if (!int.TryParse(First(args), out var tvdbId) || choice == null)
                    {
                        await responder.Ephemeral(DiscordText.ChooseSeasons);
                        return;
                    }

                    await responder.DeferAsync();
                    var result = await _requests.RequestAsync(caller, ContentType.Tv, tvdbId, choice);
                    await responder.EditAsync(RequestCards.Final(ContentType.Tv, result, SeasonText(result, choice)));
                    break;
                }

                // The single season menu on cards from before the two-path flow.
                case (InteractionKind.SelectMenu, RequestRoutes.Seasons):
                {
                    if (!int.TryParse(First(args), out var tvdbId))
                    {
                        await responder.Ephemeral(DiscordText.TitleNotFound);
                        return;
                    }

                    var choice = RequestCards.ParseSeasons(interaction.Values);
                    await responder.DeferAsync();
                    var result = await _requests.RequestAsync(caller, ContentType.Tv, tvdbId, choice);
                    await responder.EditAsync(RequestCards.Final(ContentType.Tv, result, SeasonText(result, choice)));
                    break;
                }

                case (InteractionKind.Button, RequestRoutes.Cancel):
                {
                    var result = _requests.Cancel(caller, int.TryParse(First(args), out var id) ? id : 0);

                    await (result.Success
                        ? responder.UpdateMessageAsync(new InteractionReply { Content = $"{DiscordText.FinalCancelled}{DiscordText.Dot}{result.Message}" })
                        : responder.Ephemeral(result.Message));
                    break;
                }

                // Request (movies) and Follow (both types).
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
                    await responder.EditAsync(RequestCards.Final(type.Value, result, SeasonText(result, null)));
                    break;
                }
            }
        }

        private async Task HandleSeasonStep(DiscordInteraction interaction, IInteractionResponder responder, RequestCaller caller)
        {
            if (!int.TryParse(First(interaction.CustomIdArgs), out var tvdbId))
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

            // Already requested or in the library since the card was shown: show the current state.
            if (detail.Value.Item.LibraryId.HasValue || detail.Value.Active != null || interaction.Route == RequestRoutes.TvBack)
            {
                await responder.EditAsync(RequestCards.Detail(ContentType.Tv, detail.Value));
                return;
            }

            var reply = interaction.Route switch
            {
                RequestRoutes.TvAll => RequestCards.Confirm(detail.Value, SeasonChoice.Everything),
                RequestRoutes.TvChoose => RequestCards.SeasonMenu(detail.Value),
                _ => RequestCards.Confirm(detail.Value, RequestCards.FromMenu(interaction.Values))
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
