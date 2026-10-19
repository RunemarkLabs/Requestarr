using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // /request {type} {query}: users in their own channel, or the admin in the admin channel.
    // Also the cards it produces: result picker, season picker, Request / Follow / Cancel.
    public class RequestHandler : IInteractionHandler
    {
        public const int MaxListLines = 20;

        private readonly ICallerResolver _callers;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;
        private readonly IUserMediaAccessRepository _access;
        private readonly IBackendService _backends;

        public RequestHandler(ICallerResolver callers, IRequestService requests, IUserManagementService users, IUserMediaAccessRepository access, IBackendService backends)
        {
            _callers = callers;
            _requests = requests;
            _users = users;
            _access = access;
            _backends = backends;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "request"),
            (InteractionKind.Autocomplete, "request"),
            (InteractionKind.SelectMenu, RequestRoutes.Pick),
            (InteractionKind.SelectMenu, RequestRoutes.Seasons),
            (InteractionKind.Button, RequestRoutes.Make),
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

            switch (interaction.Kind)
            {
                case InteractionKind.SlashCommand:
                    await HandleRequestCommand(interaction, responder, caller);
                    break;

                case InteractionKind.SelectMenu when interaction.Route == RequestRoutes.Pick:
                {
                    var type = RequestCards.ParseTypeKey(First(interaction.CustomIdArgs));

                    if (type == null || !int.TryParse(First(interaction.Values), out var id))
                    {
                        await responder.Ephemeral(DiscordText.TitleNotFound);
                        return;
                    }

                    await responder.DeferAsync();
                    await ShowDetail(responder, caller, type.Value, id);
                    break;
                }

                case InteractionKind.SelectMenu when interaction.Route == RequestRoutes.Seasons:
                {
                    if (!int.TryParse(First(interaction.CustomIdArgs), out var tvdbId))
                    {
                        await responder.Ephemeral(DiscordText.TitleNotFound);
                        return;
                    }

                    await responder.DeferAsync();
                    await responder.FollowupAsync(RequestCards.Outcome(await _requests.RequestAsync(caller, ContentType.Tv, tvdbId, RequestCards.ParseSeasons(interaction.Values))));
                    break;
                }

                case InteractionKind.Button when interaction.Route == RequestRoutes.Cancel:
                {
                    var result = _requests.Cancel(caller, int.TryParse(First(interaction.CustomIdArgs), out var id) ? id : 0);

                    await (result.Success
                        ? responder.UpdateMessageAsync(new InteractionReply { Content = result.Message })
                        : responder.Ephemeral(result.Message));
                    break;
                }

                case InteractionKind.Button:
                {
                    var args = interaction.CustomIdArgs;
                    var type = RequestCards.ParseTypeKey(First(args));

                    if (type == null || args.Count < 2 || !int.TryParse(args[1], out var id))
                    {
                        await responder.Ephemeral(DiscordText.TitleNotFound);
                        return;
                    }

                    await responder.DeferAsync();
                    await responder.FollowupAsync(RequestCards.Outcome(await _requests.RequestAsync(caller, type.Value, id)));
                    break;
                }
            }
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
                await ShowDetail(responder, caller, type.Value, id);
                return;
            }

            await responder.DeferAsync();
            var results = await _requests.SearchAsync(caller, type.Value, query);

            if (results.Error != null)
            {
                await responder.Ephemeral(results.Error);
                return;
            }

            await responder.FollowupAsync(RequestCards.SearchResults(type.Value, query, results.Value));
        }

        // Only types the caller can actually request: access not off (admins: types with a backend).
        // Music waits for Lidarr; comics and books are never offered yet.
        private IReadOnlyList<InteractionChoice> TypeChoices(CallerContext context, bool asAdmin, string typed)
        {
            var types = new[] { ContentType.Movie, ContentType.Tv }
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

        private async Task ShowDetail(IInteractionResponder responder, RequestCaller caller, ContentType type, int externalId)
        {
            var detail = await _requests.DetailAsync(caller, type, externalId);

            if (detail.Error != null)
            {
                await responder.Ephemeral(detail.Error);
                return;
            }

            await responder.FollowupAsync(RequestCards.Detail(type, detail.Value));
        }

        private static string First(IReadOnlyList<string> values) => values.Count > 0 ? values[0] : null;
    }
}
