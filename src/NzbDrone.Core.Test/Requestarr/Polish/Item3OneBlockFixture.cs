using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Polish
{
    // Item 3: one message per request, edited in place, ending in an unmistakable status.
    [TestFixture]
    public class Item3OneBlockFixture : PolishScenario
    {
        private static void EditedInPlace(InteractionRun run)
        {
            run.Responder.Responses.Select(r => r.Kind).Should().Equal(ResponseKind.Deferred, ResponseKind.Edit);
        }

        [Test]
        public async Task every_step_of_a_tv_request_edits_the_same_message()
        {
            var before = ChannelFor(Alice).Messages.Count;

            var results = await Harness.SendAsync(Search(Alice, "tv", "dark matter"));
            var details = await Choose(Alice, results.LastReply.SelectMenu.CustomId, DarkMatter.ToString());
            var seasons = await Click(Alice, $"req-tv-choose:{DarkMatter}");
            var confirm = await Choose(Alice, $"req-tv-pick:{DarkMatter}", "s1");
            var final = await Click(Alice, confirm.LastReply.Buttons.First().CustomId);

            foreach (var run in new[] { results, details, seasons, confirm, final })
            {
                EditedInPlace(run);
                run.LastReply.Content.Should().NotContain("> ", "no quoting of earlier bot messages");
            }

            ChannelFor(Alice).Messages.Count.Should().Be(before, "nothing else is posted in the user's channel");
            results.LastReply.Content.Should().StartWith("📺 **TV request** · 2 results");
            details.LastReply.Content.Should().StartWith("📺 **TV request** · details");
            seasons.LastReply.Content.Should().StartWith("📺 **TV request** · choose seasons");
            confirm.LastReply.Content.Should().StartWith("📺 **TV request** · confirm");
            final.LastReply.Content.Should().StartWith(DiscordText.FinalPending);
        }

        [Test]
        public async Task pending_ends_with_waiting_status_cancel_and_amber_card()
        {
            var final = (await RequestMovie(Alice, Dune)).LastReply;

            final.Content.Should().StartWith(DiscordText.FinalPending).And.Contain(DiscordText.RequestPending("Dune (2021)", 1));
            final.Buttons.Single().CustomId.Should().Be("req-cancel:1");
            final.Embeds.Single().Color.Should().Be(RequestCards.PendingColor);
        }

        [Test]
        public async Task sent_ends_with_sent_status_and_green_card()
        {
            await SetAccess(Alice, "movie", "auto");

            var final = (await RequestMovie(Alice, Dune)).LastReply;

            final.Content.Should().StartWith(DiscordText.FinalSent);
            final.Buttons.Should().BeEmpty();
            final.Embeds.Single().Color.Should().Be(RequestCards.SentColor);
        }

        [Test]
        public async Task following_ends_with_following_status()
        {
            await RequestMovie(Alice, Dune);

            var final = (await RequestMovie(Bob, Dune)).LastReply;

            final.Content.Should().StartWith(DiscordText.FinalFollowing).And.Contain(DiscordText.Subscribed("Dune (2021)", "alice", 1));
            final.Embeds.Single().Color.Should().Be(RequestCards.FollowingColor);
        }

        [Test]
        public async Task already_available_ends_with_available_status()
        {
            var final = (await RequestMovie(Alice, Arrival)).LastReply;

            final.Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain(DiscordText.AlreadyInLibrary);
        }

        [Test]
        public async Task refused_ends_with_the_reason_and_red_card()
        {
            await Harness.SendAsync(AdminCommand("user quota", Alice).WithOption("type", "movie").WithOption("limit", 1L));
            await RequestMovie(Alice, Dune);

            var final = (await RequestMovie(Alice, DuneTwo)).LastReply;

            final.Content.Should().StartWith(DiscordText.FinalRefused).And.Contain("You've reached your movies limit");
            final.Embeds.Single().Color.Should().Be(RequestCards.RefusedColor);
        }

        [Test]
        public async Task nothing_found_ends_the_block_in_place()
        {
            var run = await Harness.SendAsync(Search(Alice, "movie", "zzzz"));

            EditedInPlace(run);
            run.LastReply.Content.Should().Contain(DiscordText.FinalRefused).And.Contain(DiscordText.NothingFound);
        }

        [Test]
        public async Task details_can_go_back_to_the_results()
        {
            var results = (await Harness.SendAsync(Search(Alice, "movie", "dune"))).LastReply;
            var details = (await Choose(Alice, results.SelectMenu.CustomId, Dune.ToString())).LastReply;

            details.Buttons.Last().CustomId.Should().Be("req-res:movie:dune");

            var again = await Click(Alice, "req-res:movie:dune");

            EditedInPlace(again);
            again.LastReply.SelectMenu.Options.Select(o => o.Value).Should().Equal(Dune.ToString(), DuneTwo.ToString());
        }

        [Test]
        public async Task cancel_replaces_the_block_with_a_cancelled_status()
        {
            await RequestMovie(Alice, Dune);

            var run = await Harness.SendAsync(CancelButton(Alice, 1));

            run.Responder.Responses.Single().Kind.Should().Be(ResponseKind.MessageUpdate);
            run.LastReply.Content.Should().StartWith(DiscordText.FinalCancelled);
            run.LastReply.Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task separate_requests_are_separate_messages_with_their_own_header()
        {
            var first = await Harness.SendAsync(Search(Alice, "movie", "dune"));
            var second = await Harness.SendAsync(Search(Alice, "tv", "severance"));

            first.LastReply.Content.Should().StartWith("🎬 **Movies request**");
            second.LastReply.Content.Should().StartWith("📺 **TV request**");
            first.Responder.Should().NotBeSameAs(second.Responder);
        }
    }
}
