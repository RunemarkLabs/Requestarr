using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Gaps;

namespace Requestarr.Core.Test.AnimeDub
{
    // Item 7: a later state change deletes the old request message and posts a fresh one at the
    // bottom, mentioning its user, with only the buttons valid now.
    [TestFixture]
    public class Item7RepostFixture : GapScenario
    {
        private IRequestSubscriberRepository Subscribers => Mocker.Resolve<IRequestSubscriberRepository>();

        [Test]
        public async Task approve_deletes_the_old_message_and_posts_a_fresh_one_with_a_mention()
        {
            var message = await PendingMovie(Alice, Dune);
            await Ask(Alice, "movie", $"tmdb:{Arrival}");

            await AdminButton("req-approve:1");

            ChannelFor(Alice).MessageIds.Should().NotContain(message);
            ChannelFor(Alice).MessageIds.Last().ToString().Should().Be(RequestStore.Get(1).MessageId);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalApproved}");
            Latest(Alice).Buttons.Should().BeEmpty("nothing can be done with an approved request");
        }

        [Test]
        public async Task when_the_old_message_cannot_be_deleted_the_new_one_is_posted_and_the_old_one_loses_its_buttons()
        {
            var message = await PendingMovie(Alice, Dune);
            Discord.CannotDelete = true;

            await AdminButton("req-approve:1");

            Message(Alice, message).Buttons.Should().BeEmpty();
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalApproved}");
            RequestStore.Get(1).MessageId.Should().NotBe(message.ToString());
        }

        [Test]
        public async Task the_users_own_cancel_edits_in_place_without_a_repost()
        {
            var message = await PendingMovie(Alice, Dune);
            var count = ChannelFor(Alice).Messages.Count;

            await ClickOn(Alice, message, "req-cancel:1");

            ChannelFor(Alice).Messages.Count.Should().Be(count);
            Message(Alice, message).Content.Should().StartWith(DiscordText.FinalCancelled);
            RequestStore.Get(1).MessageId.Should().Be(message.ToString());
        }

        [Test]
        public async Task a_followers_message_is_reposted_too()
        {
            await PendingMovie(Alice, Dune);
            var bobs = await PendingMovie(Bob, Dune);
            Subscribers.ForRequest(1).Single().MessageId.Should().Be(bobs.ToString());

            await AdminButton("req-approve:1");

            ChannelFor(Bob).MessageIds.Should().NotContain(bobs);
            Latest(Bob).Content.Should().StartWith($"<@{Bob}> {DiscordText.FinalApproved}").And.Contain("You're following alice's request");
            Latest(Bob).Buttons.Should().BeEmpty();
            Subscribers.ForRequest(1).Single().MessageId.Should().Be(ChannelFor(Bob).MessageIds.Last().ToString());
        }

        [Test]
        public async Task a_follower_without_a_message_gets_the_notice()
        {
            await PendingMovie(Alice, Dune);
            await PendingMovie(Bob, Dune);
            var follower = Subscribers.ForRequest(1).Single();
            follower.MessageId = null;
            Subscribers.Update(follower);

            await AdminModal("req-deny-reason:1", RequestRoutes.ReasonField, "no");

            LastMessageIn(Bob).Should().Be($"<@{Bob}> {DiscordText.RequestDeniedNotice("Dune (2021)", 1, "no")}");
        }

        [Test]
        public async Task a_message_still_in_its_flow_is_never_reposted()
        {
            await PendingMovie(Alice, Dune);
            var card = await Ask(Bob, "movie", $"tmdb:{Dune}");
            var before = Message(Bob, card).Content;

            await AdminButton("req-approve:1");

            ChannelFor(Bob).MessageIds.Last().Should().Be(card, "nothing was posted after it");
            Message(Bob, card).Content.Should().Be(before);
        }

        [Test]
        public async Task an_old_request_without_a_stored_message_keeps_the_notice()
        {
            await RequestMovie(Alice, Dune);

            await AdminButton("req-approve:1");

            LastMessageIn(Alice).Should().Be(DiscordText.RequestApprovedNotice("Dune (2021)", 1, "Radarr"));
        }
    }
}
