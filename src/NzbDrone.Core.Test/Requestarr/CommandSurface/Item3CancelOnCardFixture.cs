using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Requests;

namespace Requestarr.Core.Test.CommandSurface
{
    // Item 3: the user's pending request cards carry a Cancel button.
    [TestFixture]
    public class Item3CancelOnCardFixture : RequestScenario
    {
        [Test]
        public async Task pending_request_reply_has_cancel_button()
        {
            var reply = (await RequestMovie(Alice, Dune)).LastReply;

            reply.Content.Should().Contain(DiscordText.RequestPending("Dune (2021)", 1));
            reply.Buttons.Single().CustomId.Should().Be("req-cancel:1");
        }

        [Test]
        public async Task auto_approved_reply_has_no_cancel_button()
        {
            await SetAccess(Alice, "movie", "auto");

            (await RequestMovie(Alice, Dune)).LastReply.Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task cancel_button_cancels_and_updates_the_card()
        {
            await RequestMovie(Alice, Dune);

            var run = await Harness.SendAsync(CancelButton(Alice, 1));

            run.Responder.Responses.Single().Kind.Should().Be(ResponseKind.MessageUpdate);
            run.LastReply.Content.Should().Contain(DiscordText.RequestCancelled("Dune (2021)", 1));
            run.LastReply.Buttons.Should().BeEmpty();
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Cancelled);
        }

        [Test]
        public async Task detail_card_for_own_pending_request_has_cancel_button()
        {
            await RequestMovie(Alice, Dune);

            var card = (await Harness.SendAsync(Search(Alice, "movie", $"tmdb:{Dune}"))).LastReply;

            card.Content.Should().Contain(DiscordText.StateYours(RequestStatus.Pending, 1));
            card.Buttons.Select(b => b.CustomId).Should().Equal("req-cancel:1");
        }

        [Test]
        public async Task cancel_by_someone_else_or_after_approval_is_refused()
        {
            await RequestMovie(Alice, Dune);

            (await Harness.SendAsync(FakeInteraction.Button("req-cancel:1").By(Bob).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Bob))))
                .LastReply.Content.Should().Contain(DiscordText.NotYourRequest);

            await AdminButton("req-approve:1");

            var late = (await Harness.SendAsync(CancelButton(Alice, 1))).LastReply;
            late.Content.Should().Be(DiscordText.CannotCancel(RequestStatus.Approved));
            late.Ephemeral.Should().BeTrue();
        }
    }
}
