using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Access;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // The admin's side of access requests: Approve… (choose the real access levels, then add them exactly as the
    // admin panel does) and Deny (an optional reason) on the card in the admin channel or in /admin requests; and
    // Unblock, on the block notice and the member card.
    public class AccessRequestHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IAccessRequestService _access;
        private readonly IUserManagementService _users;

        public AccessRequestHandler(ICallerResolver callers, IAccessRequestService access, IUserManagementService users)
        {
            _callers = callers;
            _access = access;
            _users = users;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, AccessRoutes.Approve),
            (InteractionKind.SelectMenu, AccessRoutes.Level),
            (InteractionKind.Button, AccessRoutes.Confirm),
            (InteractionKind.Button, AccessRoutes.Deny),
            (InteractionKind.ModalSubmit, AccessRoutes.DenyForm),
            (InteractionKind.Button, AccessRoutes.Unblock)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (caller.AdminDenial != null)
            {
                await responder.Ephemeral(caller.AdminDenial);
                return;
            }

            var args = interaction.CustomIdArgs;
            var admin = _users.EnsureAdminUser(interaction.UserId, interaction.Username, interaction.DisplayName);
            var actor = HandlerSupport.ActorFor(interaction, caller) with { UserId = admin.Id };
            var adminName = interaction.DisplayName ?? interaction.Username;

            if (interaction.Route == AccessRoutes.Unblock)
            {
                await UnblockAsync(interaction, responder, actor);
                return;
            }

            var request = int.TryParse(args.ElementAtOrDefault(0), out var requestId) ? _access.Find(requestId) : null;

            if (request == null)
            {
                await responder.Ephemeral(DiscordText.AccessRequestNotFound);
                return;
            }

            if (request.Status != AccessRequestState.Pending)
            {
                await responder.Ephemeral(DiscordText.AccessRequestAlreadyDecided);
                return;
            }

            await (interaction.Route switch
            {
                // A private chooser: one menu per type, starting from what they asked for.
                AccessRoutes.Approve => responder.RespondAsync(Chooser(request, AskedLevels(request))),
                AccessRoutes.Level => responder.UpdateMessageAsync(Chooser(request, ChosenLevels(interaction))),
                AccessRoutes.Confirm => ApproveAsync(responder, request, Decode(args.ElementAtOrDefault(1)), actor, adminName),
                AccessRoutes.Deny => responder.ShowModalAsync(DenyForm(request)),
                AccessRoutes.DenyForm => DenyAsync(interaction, responder, request, actor, adminName),
                _ => Task.CompletedTask
            });
        }

        private async Task UnblockAsync(DiscordInteraction interaction, IInteractionResponder responder, Actor actor)
        {
            var member = ulong.TryParse(interaction.CustomIdArgs.ElementAtOrDefault(0), out var id) ? id : 0;
            var result = await _access.UnblockAsync(member, actor);

            if (result.Success)
            {
                // The block notice (or the member card) shows the outcome instead, without the button.
                await responder.UpdateMessageAsync(new InteractionReply { Content = result.Message });
                return;
            }

            await responder.Ephemeral(result.Message);
        }

        private async Task ApproveAsync(IInteractionResponder responder, AccessRequest request, IDictionary<ContentType, AccessState> levels, Actor actor, string adminName)
        {
            await responder.DeferAsync();
            var result = await _access.ApproveAsync(request.Id, levels, actor, adminName);
            await responder.EditAsync(new InteractionReply { Content = result.Message });
        }

        private async Task DenyAsync(DiscordInteraction interaction, IInteractionResponder responder, AccessRequest request, Actor actor, string adminName)
        {
            interaction.Fields.TryGetValue(AccessRoutes.ReasonField, out var reason);
            await responder.DeferAsync(ephemeral: true);
            var result = await _access.DenyAsync(request.Id, reason, actor, adminName);
            await responder.Ephemeral(result.Message);
        }

        private static Dictionary<ContentType, AccessState> AskedLevels(AccessRequest request) =>
            ContentTypes.Offered.ToDictionary(t => t, t => request.ContentTypes.Contains(t) ? AccessState.NeedsApproval : AccessState.Off);

        // The levels the chooser carries, with the one its menu just changed.
        private static Dictionary<ContentType, AccessState> ChosenLevels(DiscordInteraction interaction)
        {
            var args = interaction.CustomIdArgs;
            var levels = Decode(args.ElementAtOrDefault(1));

            if (HandlerSupport.ParseType(args.ElementAtOrDefault(2)) is { } type && HandlerSupport.ParseLevel(interaction.Values.Count > 0 ? interaction.Values[0] : null) is { } level)
            {
                levels[type] = level;
            }

            return levels;
        }

        private static InteractionModal DenyForm(AccessRequest request) => new()
        {
            CustomId = $"{AccessRoutes.DenyForm}:{request.Id}",
            Title = DiscordText.DenyReasonTitle,
            Fields = new List<InteractionTextField> { new(AccessRoutes.ReasonField, DiscordText.DenyAccessReasonLabel, Required: false) }
        };

        private static InteractionReply Chooser(AccessRequest request, IDictionary<ContentType, AccessState> levels)
        {
            var encoded = Encode(levels);
            var menus = ContentTypes.Offered.Select(type => new InteractionSelectMenu
            {
                CustomId = $"{AccessRoutes.Level}:{request.Id}:{encoded}:{Key(type)}",
                Placeholder = DiscordText.AccessSelectPlaceholder(type),
                Options = new[] { AccessState.Off, AccessState.NeedsApproval, AccessState.AutoApprove }
                    .Select(state => new InteractionSelectOption($"{DiscordText.Label(type)}: {DiscordText.LevelLabel(state)}", LevelKey(state), null, levels[type] == state)).ToList()
            }).ToList();

            return new InteractionReply
            {
                Content = DiscordText.AccessLevelsPrompt(request.DisplayName),
                Ephemeral = true,
                SelectMenu = menus[0],
                MoreMenus = menus.Skip(1).ToList(),
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = $"{AccessRoutes.Confirm}:{request.Id}:{encoded}", Label = DiscordText.AddWithAccessLabel, Style = ButtonStyle.Success }
                }
            };
        }

        // The levels in the order of the offered types, one digit each: "110" is movies and TV needing approval, music off.
        private static string Encode(IDictionary<ContentType, AccessState> levels) =>
            string.Concat(ContentTypes.Offered.Select(t => ((int)(levels.TryGetValue(t, out var l) ? l : AccessState.Off)).ToString()));

        private static Dictionary<ContentType, AccessState> Decode(string encoded)
        {
            var levels = new Dictionary<ContentType, AccessState>();

            for (var i = 0; i < ContentTypes.Offered.Count; i++)
            {
                var digit = encoded != null && i < encoded.Length ? encoded[i] - '0' : 0;
                levels[ContentTypes.Offered[i]] = digit is >= 0 and <= 2 ? (AccessState)digit : AccessState.Off;
            }

            return levels;
        }

        private static string Key(ContentType type) => type switch
        {
            ContentType.Tv => "tv",
            ContentType.Music => "music",
            _ => "movie"
        };

        private static string LevelKey(AccessState state) => state switch
        {
            AccessState.Off => "off",
            AccessState.AutoApprove => "auto",
            _ => "approval"
        };
    }
}
