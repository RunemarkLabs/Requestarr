using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // "Add seasons" on a TV request message (its owner or a follower, in their own channel) or on
    // its admin feed card (an admin). The picker edits the message in place; adding needs no
    // approval and costs no quota. Seasons are never removed here.
    public class SeasonsHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;
        private readonly IRequestFeed _feed;

        public SeasonsHandler(ICallerResolver callers, IRequestService requests, IUserManagementService users, IRequestFeed feed)
        {
            _callers = callers;
            _requests = requests;
            _users = users;
            _feed = feed;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, RequestRoutes.AddSeasons),
            (InteractionKind.Button, RequestRoutes.AddPage),
            (InteractionKind.Button, RequestRoutes.AddConfirm),
            (InteractionKind.Button, RequestRoutes.AddBack),
            (InteractionKind.SelectMenu, RequestRoutes.AddPick)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);
            var asAdmin = caller.IsAdmin && caller.RightServer && (caller.InAdminChannel || caller.InFeedChannel);

            if (!asAdmin && caller.UserDenial != null)
            {
                await responder.Ephemeral(caller.UserDenial);
                return;
            }

            var args = interaction.CustomIdArgs;
            var request = args.Count > 0 && int.TryParse(args[0], out var id) ? _requests.Find(id) : null;

            if (request == null)
            {
                await responder.Ephemeral(DiscordText.RequestNotFound);
                return;
            }

            var user = asAdmin ? _users.EnsureAdminUser(interaction.UserId, interaction.Username, interaction.DisplayName) : caller.User;

            // Re-checked on every click: still an open TV request this person may add to.
            if (!_requests.CanAddSeasons(request, user, asAdmin))
            {
                await Back(responder, request, user, asAdmin);
                await responder.FollowupAsync(new InteractionReply { Content = request.IsActive ? DiscordText.NotYourRequest : DiscordText.CannotAddSeasons(request.Status), Ephemeral = true });
                return;
            }

            switch (interaction.Route)
            {
                case RequestRoutes.AddBack:
                    await Back(responder, request, user, asAdmin);
                    return;

                case RequestRoutes.AddConfirm when asAdmin && caller.InFeedChannel:
                    await AddFromFeedCard(interaction, responder, request, user);
                    return;

                case RequestRoutes.AddConfirm:
                    await AddFromMessage(interaction, responder, request, user, asAdmin);
                    return;

                default:
                    await ShowPicker(interaction, responder, request);
                    return;
            }
        }

        // The service reposts the feed card at the bottom; the outcome goes to the admin privately.
        private async Task AddFromFeedCard(DiscordInteraction interaction, IInteractionResponder responder, Request request, RequestarrUser user)
        {
            var add = RequestCards.DecodeSeasons(interaction.CustomIdArgs.ElementAtOrDefault(1));

            await responder.DeferAsync(ephemeral: true);
            var result = await _requests.AddSeasonsAsync(request.Id, user, true, Actor(interaction, user), add);
            await responder.FollowupAsync(new InteractionReply { Content = DiscordLimits.Truncate(result.Message, DiscordLimits.MessageLength), Ephemeral = true });

            if (!result.Success)
            {
                await _feed.PostAsync(_requests.Find(request.Id));
            }
        }

        // The request message shows the request as it is now, noting what was added.
        private async Task AddFromMessage(DiscordInteraction interaction, IInteractionResponder responder, Request request, RequestarrUser user, bool asAdmin)
        {
            var add = RequestCards.DecodeSeasons(interaction.CustomIdArgs.ElementAtOrDefault(1));

            await responder.DeferAsync();
            var outcome = await _requests.AddSeasonsAsync(request.Id, user, asAdmin, Actor(interaction, user), add);
            var updated = _requests.Find(request.Id);
            var note = outcome.Success ? DiscordText.SeasonsAddedNote(AddedSeasons(add, request)) : null;

            await responder.EditAsync(await _requests.StatusReplyForAsync(updated, user, note));

            if (!outcome.Success)
            {
                await responder.FollowupAsync(new InteractionReply { Content = outcome.Message, Ephemeral = true });
            }
        }

        // What the add brought that the request didn't already have.
        private static string AddedSeasons(SeasonChoice add, Request request) =>
            DiscordText.AddedSeasons((add?.Seasons ?? new List<int>()).Except(request.Seasons).ToList(), (add?.Future ?? false) && !request.MonitorFutureSeasons);

        // The picker: opened at its first page, paged through, or with a page's menu picked; the
        // picks ride along in the custom ID ({id}:{page}:{picks}).
        private async Task ShowPicker(DiscordInteraction interaction, IInteractionResponder responder, Request request)
        {
            var item = await _requests.ItemForAsync(request);

            if (item == null)
            {
                await responder.Ephemeral(DiscordText.TitleNotFound);
                return;
            }

            var args = interaction.CustomIdArgs;
            var page = int.TryParse(args.ElementAtOrDefault(1), out var p) ? p : 0;
            var picks = RequestCards.DecodeSeasons(args.ElementAtOrDefault(2));

            if (interaction.Route == RequestRoutes.AddPick)
            {
                var addable = RequestCards.AddableSeasons(request, item);
                var onPage = addable.Skip(page * RequestCards.SeasonsPerPage).Take(RequestCards.SeasonsPerPage).ToList();
                picks = RequestCards.WithPagePicks(picks, onPage, interaction.Values);
            }

            await responder.UpdateMessageAsync(RequestCards.AddSeasonsPicker(request, item, interaction.Route == RequestRoutes.AddSeasons ? 0 : page, picks));
        }

        // Back to the message's normal view: the request's status, or its feed card.
        private async Task Back(IInteractionResponder responder, Request request, RequestarrUser user, bool asAdmin)
        {
            await responder.UpdateMessageAsync(asAdmin ? _feed.CardFor(request) : await _requests.StatusReplyForAsync(request, user));
        }

        private static Audit.Actor Actor(DiscordInteraction interaction, RequestarrUser user) =>
            Audit.Actor.Discord(interaction.UserId, interaction.Username, user?.Id);
    }
}
