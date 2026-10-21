using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Requests
{
    // Item 8: /requests, /request cancel, /queue, /approve, /deny.
    [TestFixture]
    public class Item8ManageFixture : RequestScenario
    {
        [SetUp]
        public async Task SomeRequests()
        {
            await RequestMovie(Alice, Dune);         // #1 alice
            await RequestMovie(Bob, DuneTwo);        // #2 bob
            await RequestMovie(Alice, DuneTwo);      // alice follows #2
            await RequestTv(Bob, Severance, "s1");   // #3 bob
        }

        [Test]
        public async Task user_requests_should_show_own_and_followed_only()
        {
            var text = (await Harness.SendAsync(PanelButton(Alice, PanelRoutes.MyRequests))).LastReply.Content;

            text.Should().Contain("#1 ·").And.Contain("#2 ·").And.NotContain("#3 ·");
        }

        [Test]
        public async Task admin_queue_walks_everything_and_a_members_requests_are_on_their_card()
        {
            var walk = (await Admin("queue")).LastReply.Content;
            walk.Should().StartWith(DiscordText.WalkHeader(1, 3)).And.Contain("**alice** (asked)");
            (await Harness.SendAsync(AdminSlash("queue").WithOption("user", Bob))).LastReply.Content.Should().Contain("#2 ·").And.Contain("#3 ·").And.NotContain("#1 ·");
        }

        [Test]
        public async Task user_can_cancel_own_pending_request_only()
        {
            (await Harness.SendAsync(CancelButton(Alice, 2))).LastReply.Content.Should().Contain(DiscordText.NotYourRequest);
            (await Harness.SendAsync(CancelButton(Alice, 1))).LastReply.Content.Should().Contain(DiscordText.RequestCancelled("Dune (2021)", 1));
            (await Harness.SendAsync(CancelButton(Alice, 1))).LastReply.Content.Should().Contain(DiscordText.NothingChanged("Request #1 for Dune (2021) is cancelled."));
            (await Harness.SendAsync(CancelButton(Alice, 99))).LastReply.Content.Should().Contain(DiscordText.RequestNotFound);
        }

        [Test]
        public async Task queue_should_count_what_waits_and_end_when_nothing_does()
        {
            (await Admin("queue")).LastReply.Content.Should().StartWith(DiscordText.WalkHeader(1, 3));

            await Harness.SendAsync(AdminSlash("approve").WithOption("id", 1L));
            await Harness.SendAsync(AdminSlash("deny").WithOption("id", 2L));
            await Harness.SendAsync(AdminSlash("deny").WithOption("id", 3L));

            (await Admin("queue")).LastReply.Content.Should().Be(DiscordText.WalkDone);
        }

        [Test]
        public async Task approve_and_deny_commands()
        {
            (await Harness.SendAsync(AdminSlash("approve").WithOption("id", 1L))).LastReply.Content.Should().StartWith("Approved request #1 for Dune (2021).");
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Approved);

            (await Harness.SendAsync(AdminSlash("deny").WithOption("id", 2L).WithOption("reason", "duplicate"))).LastReply.Content.Should().Contain(DiscordText.AdminDenied(RequestStore.Get(2)));
            RequestStore.Get(2).DecisionReason.Should().Be("duplicate");

            (await Harness.SendAsync(AdminSlash("approve").WithOption("id", 42L))).LastReply.Content.Should().Contain(DiscordText.RequestNotFound);
        }

        [Test]
        public async Task admin_commands_in_a_user_channel_are_refused()
        {
            var run = await Harness.SendAsync(FakeInteraction.Button("req-approve:1").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Contain(DiscordText.AdminChannelOnly);
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Pending);
        }
    }
}
