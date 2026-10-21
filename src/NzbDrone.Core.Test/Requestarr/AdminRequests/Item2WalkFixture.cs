using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.AvailabilityFeed;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.AdminRequests
{
    // Task 3c items 1-4: /admin requests walks everything waiting on the admin, one private card at a
    // time, oldest first; acting moves to the next; handled-elsewhere items are skipped.
    [TestFixture]
    public class Item2WalkFixture : FeedScenario
    {
        private ConnectionMonitor Monitor => (ConnectionMonitor)Mocker.Resolve<IConnectionMonitor>();

        [SetUp]
        public void MonitorClock()
        {
            Monitor.Clock = () => Now;
        }

        private Task<InteractionRun> Walk() => Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin requests")));

        private Task<InteractionRun> Press(InteractionReply card, string label) =>
            Harness.SendAsync(AsAdmin(FakeInteraction.Button(card.Buttons.Single(b => b.Label == label).CustomId)));

        private static string Header(InteractionReply card) => card.Content.Split('\n')[0];

        [Test]
        public async Task nothing_waiting_says_so()
        {
            (await Walk()).LastReply.Content.Should().Be(DiscordText.WalkDone);
        }

        [Test]
        public async Task it_walks_every_kind_oldest_first_with_a_counter()
        {
            await PendingMovie(Alice, Dune);                                   // a request to approve
            Now = Now.AddMinutes(1);
            var stuck = await SentMovie(Bob, DuneTwo);                         // a download that failed
            Radarr.QueueIssue(stuck.ExternalItemId.Value, "failed", "error", "failedPending", "Unpacking failed");
            await Availability.CheckAllAsync();
            Now = Now.AddMinutes(1);
            Sonarr.Unreachable = true;                                         // an instance down
            await Monitor.CheckAllAsync();
            Now = Now.AddMinutes(5);
            await Monitor.CheckAllAsync();
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));            // a Plex link request (newest)

            var first = (await Walk()).LastReply;
            first.Ephemeral.Should().BeTrue();
            Header(first).Should().Be(DiscordText.WalkHeader(1, 4));
            first.Content.Should().Contain(DiscordText.WaitingSince(RequestStore.Get(1)));
            first.Embeds.Single().Title.Should().Be("Dune (2021)");
            first.Embeds.Single().ImageUrl.Should().NotBeNull("the full card, with its poster");
            first.Buttons.Select(b => b.Label).Should().Equal(DiscordText.ApproveLabel, DiscordText.DenyLabel, DiscordText.AlreadyAvailableLabel, DiscordText.CancelLabel, DiscordText.SkipLabel);

            var second = (await Press(first, DiscordText.SkipLabel)).LastReply;
            Header(second).Should().Be(DiscordText.WalkHeader(2, 4));
            second.Content.Should().Contain("failed: Unpacking failed").And.Contain(DiscordText.SearchAgainLabel);
            second.Buttons.Select(b => b.Label).Should().Equal(DiscordText.SearchAgainLabel, DiscordText.AlreadyAvailableLabel, DiscordText.CancelLabel, DiscordText.SkipLabel, DiscordText.BackLabel);

            var third = (await Press(second, DiscordText.SkipLabel)).LastReply;
            third.Content.Should().Contain("Sonarr can't be reached").And.Contain(DiscordText.CheckNowLabel);

            var fourth = (await Press(third, DiscordText.SkipLabel)).LastReply;
            fourth.Content.Should().Contain(DiscordText.PlexLinkWaiting("alice", Alice.ToString(), MediaServerProvider.Plex, "alice_plex", Mocker.Resolve<IMediaServerAccessRequestRepository>().Get(1).Requested));
            fourth.Buttons.Select(b => b.Label).Should().Equal(DiscordText.ApproveLinkLabel, DiscordText.DenyLinkLabel, DiscordText.SkipLabel, DiscordText.BackLabel);

            var end = (await Press(fourth, DiscordText.SkipLabel)).LastReply;
            end.Content.Should().Be(DiscordText.WalkEnd(4));
            var again = (await Press(end, DiscordText.StartOverLabel)).LastReply;
            Header(again).Should().Be(DiscordText.WalkHeader(1, 4));

            var back = (await Press(second, DiscordText.BackLabel)).LastReply;
            Header(back).Should().Be(DiscordText.WalkHeader(1, 4));
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task acting_moves_straight_to_the_next_and_says_what_happened()
        {
            await PendingMovie(Alice, Dune);
            Now = Now.AddMinutes(1);
            await PendingMovie(Bob, DuneTwo);

            var first = (await Walk()).LastReply;
            var next = (await Press(first, DiscordText.ApproveLabel)).LastReply;

            RequestStore.Get(1).Status.Should().Be(RequestStatus.Approved);
            Header(next).Should().Be(DiscordText.WalkHeader(1, 1));
            next.Content.Should().Contain("↳ Approved request #1").And.Contain(DiscordText.WaitingSince(RequestStore.Get(2)));
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalApproved}", "the user's message is reposted as usual");

            var done = (await Press(next, DiscordText.AlreadyAvailableLabel)).LastReply;
            RequestStore.Get(2).Status.Should().Be(RequestStatus.Available);
            done.Content.Should().EndWith(DiscordText.WalkDone);
        }

        [Test]
        public async Task deny_and_cancel_ask_for_a_reason_then_move_on()
        {
            await PendingMovie(Alice, Dune);
            Now = Now.AddMinutes(1);
            await PendingMovie(Bob, DuneTwo);
            var first = (await Walk()).LastReply;

            var form = await Press(first, DiscordText.DenyLabel);
            form.Responder.LastModal.CustomId.Should().StartWith($"{WalkRoutes.Form}:deny:");
            var next = (await Harness.SendAsync(AsAdmin(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId).WithField(RequestRoutes.ReasonField, "not this one")))).LastReply;

            RequestStore.Get(1).Status.Should().Be(RequestStatus.Denied);
            RequestStore.Get(1).DecisionReason.Should().Be("not this one");
            next.Content.Should().Contain(DiscordText.WaitingSince(RequestStore.Get(2)));

            var cancelForm = await Press(next, DiscordText.CancelLabel);
            var done = (await Harness.SendAsync(AsAdmin(FakeInteraction.ModalSubmit(cancelForm.Responder.LastModal.CustomId).WithField(RequestRoutes.ReasonField, "duplicate")))).LastReply;
            RequestStore.Get(2).Status.Should().Be(RequestStatus.Cancelled);
            done.Content.Should().EndWith(DiscordText.WalkDone);
        }

        [Test]
        public async Task an_item_handled_elsewhere_is_skipped_not_shown_stale()
        {
            await PendingMovie(Alice, Dune);
            Now = Now.AddMinutes(1);
            await PendingMovie(Bob, DuneTwo);
            var first = (await Walk()).LastReply;

            await AdminButton("req-approve:1");                                 // approved from its approval card meanwhile
            var next = (await Press(first, DiscordText.DenyLabel)).LastReply;

            next.Content.Should().Contain(DiscordText.AlreadyHandled).And.Contain(DiscordText.WaitingSince(RequestStore.Get(2)));
            Header(next).Should().Be(DiscordText.WalkHeader(1, 1));
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Approved, "nothing was done twice");
        }

        [Test]
        public async Task the_card_shows_everyone_on_the_request()
        {
            await Harness.SendAsync(AdminCommand("user quota", Alice).WithOption("type", "movie").WithOption("limit", 5L));
            await PendingMovie(Alice, Dune);
            var bobs = await Ask(Bob, "movie", $"tmdb:{Dune}");
            await ClickOn(Bob, bobs, $"req-follow:movie:{Dune}");
            await PendingMovie(Bob, DuneTwo);

            var card = (await Walk()).LastReply;

            card.Content.Should().Contain(DiscordText.PersonLine("alice", DiscordText.Requester, ContentType.Movie, AccessState.NeedsApproval, 1, 5, 7, 1));
            card.Content.Should().Contain(DiscordText.PersonLine("bob", DiscordText.Follower, ContentType.Movie, AccessState.NeedsApproval, 1, null, 7, 1));
        }

        [Test]
        public async Task a_failed_download_can_be_searched_again()
        {
            var stuck = await SentMovie(Alice, Dune);
            Radarr.QueueIssue(stuck.ExternalItemId.Value, "warning", "warning", "downloading", "The download is stalled with no connections");
            await Availability.CheckAllAsync();

            var card = (await Walk()).LastReply;
            card.Content.Should().Contain("is stuck: The download is stalled with no connections").And.Contain("stays in your download client");

            var next = (await Press(card, DiscordText.SearchAgainLabel)).LastReply;

            Radarr.Searches.Should().Equal(stuck.ExternalItemId.Value);
            next.Content.Should().Contain("↳ " + DiscordText.SearchingAgain(RequestStore.Get(stuck.Id), "Radarr"));
            next.Content.Should().Contain(DiscordText.WalkEnd(1), "it still waits until the download moves");
        }

        [Test]
        public async Task a_request_that_could_not_be_added_can_be_tried_again()
        {
            await SetAccess(Alice, "movie", "auto");
            Radarr.FailAdds = true;
            await RequestMovie(Alice, Dune);
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Failed);

            var card = (await Walk()).LastReply;
            card.Content.Should().Contain("couldn't be added to Radarr");
            card.Buttons.First().Label.Should().Be(DiscordText.RetrySendLabel);

            Radarr.FailAdds = false;
            var done = (await Press(card, DiscordText.RetrySendLabel)).LastReply;

            RequestStore.Get(1).Status.Should().Be(RequestStatus.Approved);
            done.Content.Should().EndWith(DiscordText.WalkDone);
            NzbDrone.Test.Common.ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public async Task a_failed_download_can_be_cancelled_or_closed_as_available()
        {
            var one = await SentMovie(Alice, Dune);
            var two = await SentMovie(Bob, DuneTwo);
            Radarr.QueueIssue(one.ExternalItemId.Value, "failed", "error", "failedPending", "x");
            Radarr.QueueIssue(two.ExternalItemId.Value, "failed", "error", "failedPending", "y");
            await Availability.CheckAllAsync();

            var card = (await Walk()).LastReply;
            var form = await Press(card, DiscordText.CancelLabel);
            var next = (await Harness.SendAsync(AsAdmin(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId).WithField(RequestRoutes.ReasonField, string.Empty)))).LastReply;
            Reload(one.Id).Status.Should().Be(RequestStatus.Cancelled);
            Radarr.Monitored[one.ExternalItemId.Value].Should().BeFalse("cancel stops the search; nothing is deleted");

            await Press(next, DiscordText.AlreadyAvailableLabel);
            Reload(two.Id).Status.Should().Be(RequestStatus.Available);
        }

        [Test]
        public async Task a_plex_link_is_decided_from_the_walk()
        {
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));

            var card = (await Walk()).LastReply;
            card.Content.Should().Contain("Movies: needs approval");
            var done = (await Press(card, DiscordText.ApproveLinkLabel)).LastReply;

            Mocker.Resolve<IMediaServerAccountRepository>().GetForUser(UserFor(Alice).Id).Single().Username.Should().Be("alice_plex");
            done.Content.Should().EndWith(DiscordText.WalkDone);
        }

        [Test]
        public async Task only_admins_in_the_admin_channel_can_walk()
        {
            (await Harness.SendAsync(FakeInteraction.SlashCommand("admin requests").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.Button($"{WalkRoutes.Do}:approve:0:r1").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
        }
    }
}
