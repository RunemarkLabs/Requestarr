using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Gaps
{
    // Gap 1: every button on a request message re-checks the request on the server. Clicked late,
    // nothing harmful happens: the asker is told privately and the message is refreshed.
    [TestFixture]
    public class Gap1LateClickFixture : GapScenario
    {
        private IRequestSubscriberRepository Subscribers => Mocker.Resolve<IRequestSubscriberRepository>();

        [Test]
        public async Task cancel_after_approval_changes_nothing_and_refreshes_the_message()
        {
            await PendingMovie(Alice, Dune);
            var message = await Ask(Alice, "movie", $"tmdb:{Dune}");   // a later card, still showing Cancel
            await AdminButton("req-approve:1");

            var run = await ClickOn(Alice, message, "req-cancel:1");

            RequestStore.Get(1).Status.Should().Be(RequestStatus.Approved);
            run.Responder.Responses[0].Kind.Should().Be(ResponseKind.MessageUpdate);
            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().Be(DiscordText.NothingChanged("Request #1 for Dune (2021) is approved."));
            Message(Alice, message).Content.Should().StartWith(DiscordText.FinalApproved);
            Message(Alice, message).Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task cancel_after_a_denial_changes_nothing()
        {
            await PendingMovie(Alice, Dune);
            var message = await Ask(Alice, "movie", $"tmdb:{Dune}");
            await AdminModal("req-deny-reason:1", RequestRoutes.ReasonField, "no");

            await ClickOn(Alice, message, "req-cancel:1");

            RequestStore.Get(1).Status.Should().Be(RequestStatus.Denied);
            Message(Alice, message).Content.Should().StartWith(DiscordText.FinalDenied);
        }

        [Test]
        public async Task someone_elses_cancel_is_refused_and_their_view_is_untouched()
        {
            await PendingMovie(Alice, Dune);

            var run = await Harness.SendAsync(CancelButton(Bob, 1));

            run.LastReply.Content.Should().Be(DiscordText.NotYourRequest);
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Pending);
        }

        [Test]
        public async Task follow_after_the_request_was_denied_creates_nothing()
        {
            await PendingMovie(Alice, Dune);
            var card = await Ask(Bob, "movie", $"tmdb:{Dune}");
            Message(Bob, card).Buttons.Single().CustomId.Should().Be($"req-follow:movie:{Dune}");
            await AdminModal("req-deny-reason:1", RequestRoutes.ReasonField, "no");

            var run = await ClickOn(Bob, card, $"req-follow:movie:{Dune}");

            RequestStore.All().Should().ContainSingle("a late Follow must not turn into a new request");
            run.LastReply.Content.Should().Be(DiscordText.NothingToFollow);
            Message(Bob, card).Content.Should().Contain(DiscordText.StatePreviouslyDenied("no"));
        }

        [Test]
        public async Task an_old_tv_follow_button_after_a_cancel_creates_nothing()
        {
            await Click(Alice, $"req-tv-all:{Severance}");
            await Harness.SendAsync(CancelButton(Alice, 1));

            var run = await Click(Bob, $"req-make:tv:{Severance}");

            RequestStore.All().Should().ContainSingle();
            run.LastReply.Content.Should().Be(DiscordText.NothingToFollow);
            Sonarr.Adds.Should().BeEmpty();
        }

        [Test]
        public async Task follow_while_still_pending_follows()
        {
            await PendingMovie(Alice, Dune);

            (await Click(Bob, $"req-follow:movie:{Dune}")).LastReply.Content.Should().StartWith(DiscordText.FinalFollowing);
            Subscribers.ForRequest(1).Should().ContainSingle();
        }

        [Test]
        public async Task all_seasons_on_a_title_requested_meanwhile_changes_nothing()
        {
            await Click(Alice, $"req-tv-all:{Severance}");

            var run = await Click(Bob, $"req-tv-all:{Severance}");

            RequestStore.All().Should().ContainSingle();
            Subscribers.ForRequest(1).Should().BeEmpty("a season button must not quietly make someone a follower");
            run.LastReply.Content.Should().StartWith(DiscordText.NothingChanged(string.Empty).TrimEnd()).And.Contain("Already requested by alice");
        }

        [TestCase("req-tv-ok:{0}:2")]
        [TestCase("req-tv-review:{0}:2")]
        [TestCase("req-tv-page:{0}:0:2")]
        [TestCase("req-tv-choose:{0}")]
        public async Task season_steps_on_a_title_requested_meanwhile_change_nothing(string customId)
        {
            await Click(Alice, $"req-tv-all:{Severance}");

            var run = await Click(Bob, string.Format(customId, Severance));

            RequestStore.All().Should().ContainSingle();
            Subscribers.ForRequest(1).Should().BeEmpty();
            run.Responder.Replies.First().Content.Should().Contain("Already requested by alice");
        }

        [Test]
        public async Task the_old_season_menu_on_a_title_requested_meanwhile_changes_nothing()
        {
            await Click(Alice, $"req-tv-all:{Severance}");

            await RequestTv(Bob, Severance, "s1");

            RequestStore.All().Should().ContainSingle();
            Subscribers.ForRequest(1).Should().BeEmpty();
        }

        [Test]
        public async Task back_to_results_on_an_old_search_only_searches_again()
        {
            await PendingMovie(Alice, Dune);

            var run = await Click(Alice, "req-res:movie:dune");

            run.LastReply.SelectMenu.Options.Should().HaveCount(2);
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Pending);
        }

        [Test]
        public async Task approve_on_a_cancelled_request_changes_nothing_and_refreshes_the_card()
        {
            await PendingMovie(Alice, Dune);
            await Harness.SendAsync(CancelButton(Alice, 1));

            var run = await AdminButton("req-approve:1");

            RequestStore.Get(1).Status.Should().Be(RequestStatus.Cancelled);
            Radarr.Adds.Should().BeEmpty();
            run.Responder.Replies.First().Content.Should().StartWith("**Request #1: cancelled** by alice");
            run.Responder.Replies.First().Buttons.Should().BeEmpty();
            run.LastReply.Content.Should().Be(DiscordText.NotPending(1, RequestStatus.Cancelled));
        }

        [Test]
        public async Task deny_on_an_approved_request_changes_nothing_and_refreshes_the_card()
        {
            await PendingMovie(Alice, Dune);
            await AdminButton("req-approve:1");

            var run = await AdminButton("req-deny:1");

            RequestStore.Get(1).Status.Should().Be(RequestStatus.Approved);
            run.Responder.LastModal.Should().BeNull();
            run.Responder.Replies.First().Content.Should().StartWith("**Request #1: approved**");
        }
    }
}
