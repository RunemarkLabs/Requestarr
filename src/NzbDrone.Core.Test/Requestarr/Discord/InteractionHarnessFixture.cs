using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Discord
{
    [TestFixture]
    public class InteractionHarnessFixture : TestBase
    {
        private const ulong AdminRoleId = 900000000000000900;

        private class EchoCommandHandler : IInteractionHandler
        {
            public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[] { (InteractionKind.SlashCommand, "request movie") };

            public Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
            {
                return responder.RespondAsync(new InteractionReply
                {
                    Content = $"{interaction.Username} asked for {interaction.GetOption<string>("query")}",
                    Buttons = new List<InteractionButton> { new() { CustomId = "approve:42", Label = "Approve", Style = ButtonStyle.Success } }
                });
            }
        }

        // Re-checks authority from the clicking user, never from the custom ID.
        private class ApproveButtonHandler : IInteractionHandler
        {
            public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[] { (InteractionKind.Button, "approve") };

            public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
            {
                if (!interaction.RoleIds.Contains(AdminRoleId))
                {
                    await responder.RespondAsync(new InteractionReply { Content = "Only the admin can approve.", Ephemeral = true });
                    return;
                }

                await responder.UpdateMessageAsync(new InteractionReply { Content = $"Request {interaction.CustomIdArgs[0]} approved" });
            }
        }

        private class SlowHandler : IInteractionHandler
        {
            public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[] { (InteractionKind.SelectMenu, "pick-season") };

            public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
            {
                await responder.DeferAsync(ephemeral: true);
                await responder.FollowupAsync(new InteractionReply { Content = "Seasons " + string.Join(",", interaction.Values), Ephemeral = true });
            }
        }

        private class ThrowingHandler : IInteractionHandler
        {
            public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[] { (InteractionKind.SlashCommand, "explode") };

            public Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
            {
                throw new InvalidOperationException("boom");
            }
        }

        private InteractionHarness _harness;

        [SetUp]
        public void Setup()
        {
            _harness = new InteractionHarness(new EchoCommandHandler(), new ApproveButtonHandler(), new SlowHandler(), new ThrowingHandler());
        }

        [Test]
        public async Task should_route_slash_command_with_options_and_caller()
        {
            var run = await _harness.SendAsync(FakeInteraction.SlashCommand("/request movie").WithOption("query", "Dune").By(123, "alice"));

            run.Outcome.Should().Be(DispatchOutcome.Handled);
            run.LastReply.Content.Should().Be("alice asked for Dune");
            run.LastReply.Buttons.Single().CustomId.Should().Be("approve:42");
        }

        [Test]
        public async Task should_route_button_click_by_custom_id_prefix()
        {
            var run = await _harness.SendAsync(FakeInteraction.Button("approve:42").WithRoles(AdminRoleId));

            run.Outcome.Should().Be(DispatchOutcome.Handled);
            run.Responder.Responses.Single().Kind.Should().Be(ResponseKind.MessageUpdate);
            run.LastReply.Content.Should().Be("Request 42 approved");
        }

        [Test]
        public async Task button_click_from_non_admin_should_be_rejected_by_handler()
        {
            var run = await _harness.SendAsync(FakeInteraction.Button("approve:42"));

            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().Be("Only the admin can approve.");
        }

        [Test]
        public async Task should_support_defer_then_followup_for_select_menus()
        {
            var run = await _harness.SendAsync(FakeInteraction.SelectMenu("pick-season:7", "1", "2"));

            run.Responder.Responses.Select(r => r.Kind).Should().Equal(ResponseKind.Deferred, ResponseKind.Followup);
            run.LastReply.Content.Should().Be("Seasons 1,2");
        }

        [Test]
        public async Task unknown_command_should_get_ephemeral_reply()
        {
            var run = await _harness.SendAsync(FakeInteraction.SlashCommand("nope"));

            run.Outcome.Should().Be(DispatchOutcome.Unhandled);
            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().Be(InteractionDispatcher.UnknownInteractionMessage);
        }

        [Test]
        public async Task handler_failure_should_be_reported_to_user_without_details()
        {
            var run = await _harness.SendAsync(FakeInteraction.SlashCommand("explode"));

            run.Outcome.Should().Be(DispatchOutcome.Failed);
            run.LastReply.Content.Should().Be(InteractionDispatcher.FailedInteractionMessage);
            run.LastReply.Content.Should().NotContain("boom");

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void duplicate_routes_should_be_rejected()
        {
            Action act = () => new InteractionHarness(new EchoCommandHandler(), new EchoCommandHandler());

            act.Should().Throw<InvalidOperationException>().WithMessage("*request movie*");
        }

        [Test]
        public async Task recording_responder_should_reject_second_initial_response()
        {
            var responder = new RecordingResponder();
            await responder.RespondAsync(new InteractionReply { Content = "one" });

            Func<Task> act = () => responder.RespondAsync(new InteractionReply { Content = "two" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
