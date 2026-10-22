using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // Removals in Discord: the requester's Yes / No and every voter's Keep it (in their own channels),
    // the requester's Remove… on their request message, and the admin's Delete (feed card), Remove a
    // title (admin panel, searching every library), Force and Drop.
    public class RemovalHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IRemovalService _removals;
        private readonly IRequestService _requests;

        public RemovalHandler(ICallerResolver callers, IRemovalService removals, IRequestService requests)
        {
            _callers = callers;
            _removals = removals;
            _requests = requests;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, RemovalRoutes.Answer),
            (InteractionKind.Button, RemovalRoutes.Keep),
            (InteractionKind.Button, RemovalRoutes.Unkeep),
            (InteractionKind.Button, RemovalRoutes.User),
            (InteractionKind.SelectMenu, RemovalRoutes.UserScope),
            (InteractionKind.Button, RemovalRoutes.UserGo),
            (InteractionKind.Button, RemovalRoutes.Feed),
            (InteractionKind.Button, RemovalRoutes.Admin),
            (InteractionKind.SelectMenu, RemovalRoutes.AdminScope),
            (InteractionKind.Button, RemovalRoutes.AdminGo),
            (InteractionKind.Button, RemovalRoutes.AdminForce),
            (InteractionKind.Button, RemovalRoutes.AdminForceYes),
            (InteractionKind.Button, RemovalRoutes.Force),
            (InteractionKind.Button, RemovalRoutes.ForceYes),
            (InteractionKind.Button, RemovalRoutes.Drop),
            (InteractionKind.Button, RemovalRoutes.Search),
            (InteractionKind.ModalSubmit, RemovalRoutes.SearchForm),
            (InteractionKind.SelectMenu, RemovalRoutes.Pick)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);
            var args = interaction.CustomIdArgs;
            var actor = HandlerSupport.ActorFor(interaction, caller);

            switch (interaction.Route)
            {
                // Users, in their own channels.
                case RemovalRoutes.Answer:
                case RemovalRoutes.Keep:
                case RemovalRoutes.Unkeep:
                case RemovalRoutes.User:
                case RemovalRoutes.UserScope:
                case RemovalRoutes.UserGo:
                {
                    if (caller.UserDenial != null)
                    {
                        await responder.Ephemeral(caller.UserDenial);
                        return;
                    }

                    await HandleUser(interaction, responder, caller.User, actor, args);
                    return;
                }

                default:
                {
                    var denial = interaction.Route is RemovalRoutes.Search or RemovalRoutes.SearchForm or RemovalRoutes.Pick ? caller.AdminDenial : caller.AdminButtonDenial;

                    if (denial != null)
                    {
                        await responder.Ephemeral(denial);
                        return;
                    }

                    await HandleAdmin(interaction, responder, caller, actor, args);
                    return;
                }
            }
        }

        private async Task HandleUser(DiscordInteraction interaction, IInteractionResponder responder, RequestarrUser user, Audit.Actor actor, IReadOnlyList<string> args)
        {
            var id = args.Count > 0 && int.TryParse(args[0], out var parsed) ? parsed : 0;

            switch (interaction.Route)
            {
                case RemovalRoutes.Answer:
                {
                    await responder.DeferAsync();
                    var result = await _removals.AnswerAsync(id, user, actor, args.Count > 1 && args[1] == "y");

                    if (!result.Success)
                    {
                        await responder.Ephemeral(result.Message);
                    }

                    return;
                }

                case RemovalRoutes.Keep:
                case RemovalRoutes.Unkeep:
                {
                    var keep = interaction.Route == RemovalRoutes.Keep;
                    var result = await _removals.SetKeepAsync(id, user, actor, keep);
                    var proposal = _removals.Find(id);

                    if (!result.Success || proposal == null)
                    {
                        await responder.Ephemeral(result.Message);
                        return;
                    }

                    // The prompt changes in place: their keep is noted (and can be taken back).
                    await responder.UpdateMessageAsync(new InteractionReply
                    {
                        Content = $"<@{interaction.UserId}> {DiscordText.RemovalPrompt(proposal, proposal.WindowClosesAt.Value, keep)}",
                        KeepEmbeds = true,
                        Buttons = RemovalService.KeepButtons(proposal, keep)
                    });
                    await responder.Ephemeral(result.Message);
                    return;
                }

                case RemovalRoutes.User:
                case RemovalRoutes.UserScope:
                {
                    var request = _requests.Find(id);

                    if (request == null || request.OwnerUserId != user.Id)
                    {
                        await responder.Ephemeral(DiscordText.RemovalOnlyYours);
                        return;
                    }

                    var target = await _removals.TargetForRequestAsync(id);

                    if (target.Error != null)
                    {
                        await responder.Ephemeral(target.Error);
                        return;
                    }

                    var scope = interaction.Route == RemovalRoutes.UserScope ? FromMenu(interaction.Values) : new List<int>();
                    var card = UserCard(request, target.Value, scope);

                    if (interaction.Route == RemovalRoutes.User)
                    {
                        await responder.RespondAsync(card);
                    }
                    else
                    {
                        await responder.UpdateMessageAsync(card);
                    }

                    return;
                }

                case RemovalRoutes.UserGo:
                {
                    var result = await _removals.StartByUserAsync(user, actor, id, RemovalRoutes.Decode(args.Count > 1 ? args[1] : null));
                    await responder.UpdateMessageAsync(new InteractionReply { Content = result.Message });
                    return;
                }
            }
        }

        private async Task HandleAdmin(DiscordInteraction interaction, IInteractionResponder responder, CallerContext caller, Audit.Actor actor, IReadOnlyList<string> args)
        {
            var adminName = interaction.DisplayName ?? interaction.Username;
            int Arg(int i) => args.Count > i && int.TryParse(args[i], out var value) ? value : 0;

            switch (interaction.Route)
            {
                case RemovalRoutes.Search:
                    await responder.ShowModalAsync(new InteractionModal
                    {
                        CustomId = RemovalRoutes.SearchForm,
                        Title = DiscordText.RemoveSearchTitle,
                        Fields = new List<InteractionTextField> { new(RemovalRoutes.SearchField, DiscordText.RemoveSearchField, Required: true, MaxLength: 100, Paragraph: false) }
                    });
                    return;

                case RemovalRoutes.SearchForm:
                {
                    interaction.Fields.TryGetValue(RemovalRoutes.SearchField, out var term);
                    var found = await _removals.SearchLibraryAsync(term);

                    if (found.Count == 0)
                    {
                        await responder.Ephemeral(DiscordText.RemovalNothingFound);
                        return;
                    }

                    await responder.RespondAsync(new InteractionReply
                    {
                        Content = DiscordText.RemovalSearchResults(term, found.Count),
                        Ephemeral = true,
                        SelectMenu = new InteractionSelectMenu
                        {
                            CustomId = RemovalRoutes.Pick,
                            Placeholder = DiscordText.RemovalPickPlaceholder,
                            Options = found.Select(t => new InteractionSelectOption(DiscordText.RemovalChoice(t), $"{t.Instance.Id}:{t.Title.Id}")).ToList()
                        }
                    });
                    return;
                }

                case RemovalRoutes.Pick:
                case RemovalRoutes.Feed:
                case RemovalRoutes.Admin:
                case RemovalRoutes.AdminScope:
                {
                    var target = interaction.Route switch
                    {
                        RemovalRoutes.Pick => await Target(interaction.Values.Count > 0 ? interaction.Values[0] : null),
                        RemovalRoutes.Feed => await _removals.TargetForRequestAsync(Arg(0)),
                        _ => await _removals.TargetAsync(Arg(0), Arg(1))
                    };

                    if (target.Error != null)
                    {
                        await responder.Ephemeral(target.Error);
                        return;
                    }

                    var card = AdminCard(target.Value, interaction.Route == RemovalRoutes.AdminScope ? FromMenu(interaction.Values) : new List<int>());

                    if (interaction.Route is RemovalRoutes.Feed)
                    {
                        await responder.RespondAsync(card);
                    }
                    else
                    {
                        await responder.UpdateMessageAsync(card);
                    }

                    return;
                }

                case RemovalRoutes.AdminForce:
                {
                    var target = await _removals.TargetAsync(Arg(0), Arg(1));
                    var seasons = RemovalRoutes.Decode(args.Count > 2 ? args[2] : null);

                    await responder.UpdateMessageAsync(new InteractionReply
                    {
                        Content = DiscordText.RemovalConfirmForce(target.Value?.Label ?? "it", seasons),
                        Buttons = new List<InteractionButton>
                        {
                            new() { CustomId = $"{RemovalRoutes.AdminForceYes}:{Arg(0)}:{Arg(1)}:{RemovalRoutes.Encode(seasons)}", Label = DiscordText.ConfirmDeleteLabel, Style = ButtonStyle.Danger },
                            new() { CustomId = $"{RemovalRoutes.Admin}:{Arg(0)}:{Arg(1)}", Label = DiscordText.BackLabel, Style = ButtonStyle.Secondary }
                        }
                    });
                    return;
                }

                case RemovalRoutes.AdminGo:
                case RemovalRoutes.AdminForceYes:
                {
                    await responder.DeferAsync();
                    var result = await _removals.StartByAdminAsync(actor, adminName, caller.User?.Id, Arg(0), Arg(1), RemovalRoutes.Decode(args.Count > 2 ? args[2] : null), interaction.Route == RemovalRoutes.AdminForceYes);
                    await responder.EditAsync(new InteractionReply { Content = result.Message });
                    return;
                }

                case RemovalRoutes.Force:
                {
                    var proposal = _removals.Find(Arg(0));

                    if (proposal == null || !proposal.IsOpen)
                    {
                        await responder.Ephemeral(proposal == null ? DiscordText.RemovalNotFound : DiscordText.RemovalAlreadyMoved(proposal));
                        return;
                    }

                    await responder.RespondAsync(new InteractionReply
                    {
                        Content = DiscordText.RemovalConfirmForce(proposal.Label, proposal.Seasons),
                        Ephemeral = true,
                        Buttons = new List<InteractionButton> { new() { CustomId = $"{RemovalRoutes.ForceYes}:{proposal.Id}", Label = DiscordText.ConfirmDeleteLabel, Style = ButtonStyle.Danger } }
                    });
                    return;
                }

                case RemovalRoutes.ForceYes:
                {
                    await responder.DeferAsync();
                    var result = await _removals.ForceAsync(Arg(0), actor, adminName, caller.User?.Id);
                    await responder.EditAsync(new InteractionReply { Content = result.Message });
                    return;
                }

                case RemovalRoutes.Drop:
                    await responder.Reply(await _removals.DropAsync(Arg(0), actor, adminName, caller.User?.Id));
                    return;
            }
        }

        private Task<LookupOutcome<RemovalTarget>> Target(string value)
        {
            var parts = (value ?? string.Empty).Split(':');
            return parts.Length == 2 && int.TryParse(parts[0], out var backend) && int.TryParse(parts[1], out var item)
                ? _removals.TargetAsync(backend, item)
                : Task.FromResult(LookupOutcome<RemovalTarget>.Fail(DiscordText.RemovalNotInLibrary));
        }

        // The admin's removal card: who's asked, what's removed (TV: the whole show or chosen seasons), Start or Force.
        private static InteractionReply AdminCard(RemovalTarget target, List<int> seasons)
        {
            var at = $"{target.Instance.Id}:{target.Title.Id}";
            var scope = RemovalRoutes.Encode(seasons);
            var reply = new InteractionReply
            {
                Content = DiscordText.RemovalStartCard(target, seasons),
                Ephemeral = true,
                SelectMenu = ScopeMenu($"{RemovalRoutes.AdminScope}:{at}", target, seasons),
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = $"{RemovalRoutes.AdminGo}:{at}:{scope}", Label = DiscordText.StartRemovalLabel, Style = ButtonStyle.Primary },
                    new() { CustomId = $"{RemovalRoutes.AdminForce}:{at}:{scope}", Label = DiscordText.ForceNowLabel, Style = ButtonStyle.Danger }
                }
            };

            return reply;
        }

        private static InteractionReply UserCard(Request request, RemovalTarget target, List<int> seasons) => new()
        {
            Content = DiscordText.RemovalUserCard(request, seasons),
            Ephemeral = true,
            SelectMenu = ScopeMenu($"{RemovalRoutes.UserScope}:{request.Id}", target, seasons),
            Buttons = new List<InteractionButton>
            {
                new() { CustomId = $"{RemovalRoutes.UserGo}:{request.Id}:{RemovalRoutes.Encode(seasons)}", Label = DiscordText.StartVoteLabel, Style = ButtonStyle.Danger }
            }
        };

        // TV: the whole show, or any of the seasons that have files. Movies have no menu.
        private static InteractionSelectMenu ScopeMenu(string customId, RemovalTarget target, List<int> seasons)
        {
            if (target.Type != ContentType.Tv)
            {
                return null;
            }

            var options = new List<InteractionSelectOption> { new(DiscordText.WholeShowOption, "all", null, seasons.Count == 0) };
            options.AddRange(target.Title.SeasonsWithFiles.Where(s => s > 0).Take(DiscordLimits.ChoicesPerMenu - 1)
                .Select(s => new InteractionSelectOption(DiscordText.SeasonLabel(s), s.ToString(), null, seasons.Contains(s))));

            return new InteractionSelectMenu { CustomId = customId, Placeholder = DiscordText.RemovalScopePlaceholder, MinValues = 1, MaxValues = options.Count, Options = options };
        }

        private static List<int> FromMenu(IReadOnlyList<string> values) =>
            values.Contains("all") ? new List<int>() : values.Select(v => int.TryParse(v, out var s) ? s : -1).Where(s => s >= 0).Distinct().OrderBy(s => s).ToList();
    }
}
