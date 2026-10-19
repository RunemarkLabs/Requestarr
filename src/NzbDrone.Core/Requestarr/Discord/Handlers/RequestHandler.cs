using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // Requesting: users in their own channel, or the admin in the admin channel.
    public class RequestHandler : IInteractionHandler
    {
        public const int MaxListLines = 20;

        private readonly ICallerResolver _callers;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;

        public RequestHandler(ICallerResolver callers, IRequestService requests, IUserManagementService users)
        {
            _callers = callers;
            _requests = requests;
            _users = users;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "request movie"),
            (InteractionKind.SlashCommand, "request tv"),
            (InteractionKind.SlashCommand, "request movie-tmdb"),
            (InteractionKind.SlashCommand, "request tv-tvdb"),
            (InteractionKind.SlashCommand, "request cancel"),
            (InteractionKind.SlashCommand, "requests"),
            (InteractionKind.SlashCommand, "quota"),
            (InteractionKind.SelectMenu, RequestRoutes.Pick),
            (InteractionKind.SelectMenu, RequestRoutes.Seasons),
            (InteractionKind.Button, RequestRoutes.Make)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var context = _callers.Resolve(interaction);
            var asAdmin = context.RightServer && context.IsAdmin && context.InAdminChannel;

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
                    await HandleCommand(interaction, responder, caller, asAdmin);
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
                    await responder.Reply(await _requests.RequestAsync(caller, ContentType.Tv, tvdbId, RequestCards.ParseSeasons(interaction.Values)));
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
                    await responder.Reply(await _requests.RequestAsync(caller, type.Value, id));
                    break;
                }
            }
        }

        private async Task HandleCommand(DiscordInteraction interaction, IInteractionResponder responder, RequestCaller caller, bool asAdmin)
        {
            switch (interaction.CommandName.ToLowerInvariant())
            {
                case "request movie":
                case "request tv":
                {
                    var type = interaction.CommandName.EndsWith("tv", StringComparison.OrdinalIgnoreCase) ? ContentType.Tv : ContentType.Movie;
                    var query = interaction.GetOption<string>("query")?.Trim();

                    if (string.IsNullOrWhiteSpace(query))
                    {
                        await responder.Ephemeral(DiscordText.NothingFound);
                        return;
                    }

                    await responder.DeferAsync();
                    var results = await _requests.SearchAsync(caller, type, query);

                    if (results.Error != null)
                    {
                        await responder.Ephemeral(results.Error);
                        return;
                    }

                    await responder.FollowupAsync(RequestCards.SearchResults(type, query, results.Value));
                    break;
                }

                case "request movie-tmdb":
                case "request tv-tvdb":
                {
                    var type = interaction.CommandName.EndsWith("tvdb", StringComparison.OrdinalIgnoreCase) ? ContentType.Tv : ContentType.Movie;
                    var id = interaction.GetInteger("id");

                    if (id is null or <= 0)
                    {
                        await responder.Ephemeral(DiscordText.TitleNotFound);
                        return;
                    }

                    await responder.DeferAsync();
                    await ShowDetail(responder, caller, type, (int)id.Value);
                    break;
                }

                case "request cancel":
                    await responder.Reply(_requests.Cancel(caller, (int)(interaction.GetInteger("id") ?? 0)));
                    break;

                case "requests":
                {
                    var filterUser = ResolveUserFilter(interaction, asAdmin, caller);
                    var status = Enum.TryParse<RequestStatus>(interaction.GetOption<string>("status"), true, out var s) ? s : (RequestStatus?)null;
                    var type = HandlerSupport.ParseType(interaction.GetOption<string>("type"));
                    var list = _requests.List(filterUser, asAdmin && filterUser == null, status, type);

                    await responder.Public(list.Any()
                        ? string.Join("\n", list.Take(MaxListLines).Select(r => DiscordText.RequestLine(r, _requests.OwnerName(r), asAdmin)))
                        : DiscordText.NoRequests);
                    break;
                }

                case "quota":
                {
                    var target = ResolveUserFilter(interaction, asAdmin, caller) ?? caller.User;

                    await responder.Public(_requests.QuotaSummary(target));
                    break;
                }
            }
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

        // Only the admin (in the admin channel) may look at someone else; users always see their own.
        private RequestarrUser ResolveUserFilter(DiscordInteraction interaction, bool asAdmin, RequestCaller caller)
        {
            var other = interaction.GetSnowflake("user");

            if (!asAdmin)
            {
                return caller.User;
            }

            return other == null ? null : _users.FindByDiscordId(other.Value);
        }
    }
}
