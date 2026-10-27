using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Qa;

namespace Requestarr.Core.Discord.Handlers
{
    // The QA checklist's Passes and Failing buttons (admins only), and the bug report flow anyone in the server can use:
    // the Report a problem button (or a failing checklist item), a menu of Requestarr's parts, then a short form.
    public class QaHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IQaChecklistService _checklist;
        private readonly IBugReportService _reports;

        public QaHandler(ICallerResolver callers, IQaChecklistService checklist, IBugReportService reports)
        {
            _callers = callers;
            _checklist = checklist;
            _reports = reports;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, QaRoutes.Check),
            (InteractionKind.Button, QaRoutes.Report),
            (InteractionKind.SelectMenu, QaRoutes.Part),
            (InteractionKind.ModalSubmit, QaRoutes.Form)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (!caller.RightServer)
            {
                await responder.Ephemeral(DiscordText.WrongServer);
                return;
            }

            await (interaction.Route switch
            {
                QaRoutes.Check => CheckAsync(interaction, responder, caller.IsAdmin),
                QaRoutes.Report => OpenReportAsync(interaction, responder, null, limited: true),
                QaRoutes.Part => responder.ShowModalAsync(ReportForm(interaction)),
                QaRoutes.Form => SubmitAsync(interaction, responder, caller.IsAdmin),
                _ => Task.CompletedTask
            });
        }

        private async Task CheckAsync(DiscordInteraction interaction, IInteractionResponder responder, bool isAdmin)
        {
            var args = interaction.CustomIdArgs;
            var key = args.ElementAtOrDefault(1);

            if (!isAdmin)
            {
                await responder.Ephemeral(QaText.AdminsOnly);
                return;
            }

            if (key == null || _checklist.Find(key) is not { } entry)
            {
                await responder.Ephemeral(QaText.ItemGone);
                return;
            }

            // An admin working through the checklist isn't held to the members' report limit.
            if (args.ElementAtOrDefault(0) == "fail")
            {
                await OpenReportAsync(interaction, responder, entry, limited: false);
                return;
            }

            // The card itself shows the pass (who and when), so the press needs no reply of its own. The card
            // can't be edited into a plain message anyway: Discord keeps a card a card.
            await responder.DeferAsync();
            await _checklist.MarkPassedAsync(key, interaction.UserId, interaction.DisplayName);
        }

        private static InteractionModal ReportForm(DiscordInteraction interaction)
        {
            var component = interaction.Values.Count > 0 ? interaction.Values[0] : "other";
            var key = interaction.CustomIdArgs.ElementAtOrDefault(0);

            return new InteractionModal
            {
                CustomId = $"{QaRoutes.Form}:{component}{(string.IsNullOrEmpty(key) ? string.Empty : ":" + key)}",
                Title = QaText.FormTitle,
                Fields = new List<InteractionTextField>
                {
                    new(QaRoutes.WhatField, QaText.WhatLabel, Required: true, MaxLength: BugReportService.FieldLength),
                    new(QaRoutes.ExpectedField, QaText.ExpectedLabel, Required: false, MaxLength: BugReportService.FieldLength)
                }
            };
        }

        private async Task SubmitAsync(DiscordInteraction interaction, IInteractionResponder responder, bool isAdmin)
        {
            var args = interaction.CustomIdArgs;
            interaction.Fields.TryGetValue(QaRoutes.WhatField, out var what);
            interaction.Fields.TryGetValue(QaRoutes.ExpectedField, out var expected);

            var key = args.ElementAtOrDefault(1);
            var fromChecklist = key != null && isAdmin;

            // The private menu becomes the answer, so no new private message lands above the report's card.
            await responder.UpdateMessageAsync(new InteractionReply { Content = QaText.Sending, Ephemeral = true });
            var result = await _reports.SubmitAsync(interaction.UserId, interaction.DisplayName, args.ElementAtOrDefault(0), what, expected, key, limited: !fromChecklist);

            // From the bug reports channel the new card is the answer, and the private menu goes: it replies to
            // the Report a problem panel, which moves below the card and would leave it pointing at nothing.
            if (result.Report != null && key == null)
            {
                await responder.DeleteOriginalAsync();
                return;
            }

            await responder.EditAsync(new InteractionReply { Content = result.Message, Ephemeral = true });
        }

        // The part menu, privately, saying first whether the report becomes public; or how long to wait.
        private async Task OpenReportAsync(DiscordInteraction interaction, IInteractionResponder responder, QaChecklistEntry item, bool limited)
        {
            if (limited && _reports.WaitLeft(interaction.UserId) is { } wait)
            {
                await responder.Ephemeral(QaText.Wait(wait));
                return;
            }

            await responder.RespondAsync(new InteractionReply
            {
                Ephemeral = true,
                Content = QaText.PartPrompt(_reports.FilesIssues, item?.Name),
                SelectMenu = new InteractionSelectMenu
                {
                    CustomId = $"{QaRoutes.Part}{(item != null ? ":" + item.Key : string.Empty)}",
                    Placeholder = QaText.PartPlaceholder,
                    Options = BugReportComponents.All.Select(c => new InteractionSelectOption(c.Name, c.Key)).ToList()
                }
            });
        }
    }
}
