using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // Item 5: off refuses, needs approval queues a card, auto-approve sends immediately.
    [TestFixture]
    public class Item5ApprovalFixture : RequestScenario
    {
        [Test]
        public async Task access_off_should_refuse()
        {
            await SetAccess(Alice, "movie", "off");

            (await RequestMovie(Alice, Dune)).LastReply.Content.Should().Contain(DiscordText.AccessOff(ContentType.Movie));
            RequestStore.All().Should().BeEmpty();
        }

        [Test]
        public async Task needs_approval_should_create_pending_request_and_admin_card()
        {
            var run = await RequestMovie(Alice, Dune);

            run.LastReply.Content.Should().Contain(DiscordText.RequestPending("Dune (2021)", 1));
            LastRequest.Status.Should().Be(RequestStatus.Pending);
            Radarr.Adds.Should().BeEmpty();

            var card = LastAdminMessage;
            card.Content.Should().Contain("Request #1").And.Contain("Dune (2021)").And.Contain("alice");
            card.Buttons.Select(b => b.CustomId).Should().Equal("req-approve:1", "req-deny:1", "req-available:1");
        }

        [Test]
        public async Task approve_button_should_send_to_backend_update_card_and_notify_user()
        {
            await RequestMovie(Alice, Dune);

            var run = await AdminButton("req-approve:1");

            run.Responder.Responses.Single().Kind.Should().Be(ResponseKind.MessageUpdate);
            run.LastReply.Content.Should().Contain("approved").And.Contain("owner");
            run.LastReply.Buttons.Should().BeEmpty();
            LastRequest.Status.Should().Be(RequestStatus.Approved);
            Radarr.Adds.Should().ContainSingle();
            LastMessageIn(Alice).Should().Be(DiscordText.RequestApprovedNotice("Dune (2021)", 1, "Radarr"));
        }

        [Test]
        public async Task deny_button_should_ask_for_reason_then_deny()
        {
            await RequestMovie(Alice, Dune);

            var modalRun = await AdminButton("req-deny:1");
            modalRun.Responder.Responses.Single().Kind.Should().Be(ResponseKind.Modal);
            modalRun.Responder.LastModal.CustomId.Should().Be("req-deny-reason:1");

            var submit = await Harness.SendAsync(FakeInteraction.ModalSubmit("req-deny-reason:1").WithField("reason", "Already on the server in 4K")
                .By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            submit.LastReply.Content.Should().Contain("denied").And.Contain("Already on the server in 4K");
            LastRequest.Status.Should().Be(RequestStatus.Denied);
            LastRequest.DecisionReason.Should().Be("Already on the server in 4K");
            LastMessageIn(Alice).Should().Be(DiscordText.RequestDeniedNotice("Dune (2021)", 1, "Already on the server in 4K"));
            Radarr.Adds.Should().BeEmpty();
        }

        [Test]
        public async Task auto_approve_should_send_immediately()
        {
            await SetAccess(Alice, "movie", "auto");

            var run = await RequestMovie(Alice, Dune);

            run.LastReply.Content.Should().Contain(DiscordText.RequestSent("Dune (2021)", 1, "Radarr"));
            LastRequest.Status.Should().Be(RequestStatus.Approved);
            Discord.Channels[AdminChannel].Messages.Should().NotContain(m => m.Content.Contains("Request #1"));
        }

        [Test]
        public async Task approval_buttons_by_non_admin_or_twice_are_refused()
        {
            await RequestMovie(Alice, Dune);

            (await Harness.SendAsync(FakeInteraction.Button("req-approve:1").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).LastReply.Content.Should().Contain(DiscordText.AdminOnly);

            await AdminButton("req-approve:1");
            (await AdminButton("req-approve:1")).LastReply.Content.Should().Contain(DiscordText.NotPending(1, RequestStatus.Approved));
            (await AdminButton("req-deny:1")).LastReply.Content.Should().Contain(DiscordText.NotPending(1, RequestStatus.Approved));
        }
    }
}
