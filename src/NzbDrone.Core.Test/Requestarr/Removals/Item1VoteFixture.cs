using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.Removals
{
    // Task 4 items 1, 4 to 6, 8 to 10: the requester asks to remove their title; everyone else with
    // access gets "still watching?"; one keep keeps it; no keeps at the close deletes it, without
    // excluding it; everyone asked sees the outcome. Nothing changes while the vote is open.
    [TestFixture]
    public class Item1VoteFixture : RemovalScenario
    {
        [Test]
        public async Task the_requester_removes_their_title_from_its_message_and_it_goes_straight_to_the_vote()
        {
            var request = await DownloadedMovie();
            Latest(Alice).Buttons.Select(b => b.Label).Should().Contain(DiscordText.RemoveRequestLabel);

            var card = (await UserRemove(Alice, request.Id)).LastReply;
            card.Ephemeral.Should().BeTrue();
            card.Content.Should().Contain("Remove **Dune (2021)**?").And.Contain("nothing changes until the vote closes");
            card.SelectMenu.Should().BeNull("a movie is removed whole");
            var started = (await Click(Alice, card.Buttons.Single().CustomId)).LastReply;

            started.Content.Should().StartWith("Started:");
            Proposal().Status.Should().Be(RemovalStatus.Voting);
            Proposal().WindowClosesAt.Should().Be(Now.AddDays(7), "the default vote window");
            MessagesOf(Alice, "Up for a vote").Should().ContainSingle("their own channel says it's up for a vote");
            PromptOf(Alice).Should().BeNull("the one who asked isn't asked to keep it");

            var bobs = PromptOf(Bob);
            bobs.Content.Should().StartWith($"<@{Bob}> 🗳️ **Still watching?**").And.Contain("**Dune (2021)**").And.Contain($"<t:{new DateTimeOffset(Now.AddDays(7)).ToUnixTimeSeconds()}:f>");
            bobs.Buttons.Select(b => b.Label).Should().Equal(DiscordText.RemovalKeepLabel);
            bobs.Embeds.Single().Title.Should().Be("Dune (2021)", "the title's card");

            Radarr.Deletes.Should().BeEmpty("nothing changes during a vote");
            Radarr.Monitored.Should().NotContainKey(request.ExternalItemId.Value, "not even unmonitored");
            Line(CardOf(request.Id), "🗑️ Remov").Should().StartWith("🗑️ Removal vote until").And.EndWith("0 keep");
            ButtonIdsOf(CardOf(request.Id)).Should().Equal("rm-force:1", "rm-drop:1");
        }

        [Test]
        public async Task only_the_requester_can_remove_their_title_and_only_once_downloaded()
        {
            await PendingMovie(Alice, DuneTwo);
            Latest(Alice).Buttons.Select(b => b.Label).Should().NotContain(DiscordText.RemoveRequestLabel, "nothing is downloaded yet");

            var request = await DownloadedMovie();

            (await UserRemove(Bob, request.Id)).LastReply.Content.Should().Be(DiscordText.RemovalOnlyYours);
            (await UserStartVote(Bob, request.Id)).LastReply.Content.Should().Be(DiscordText.RemovalOnlyYours);
            Proposals.All().Should().BeEmpty();
        }

        [Test]
        public async Task no_keeps_when_the_window_closes_deletes_the_title_without_excluding_it()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);

            await Tick(TimeSpan.FromDays(6));
            Radarr.Deletes.Should().BeEmpty("the window is still open");
            await Tick(TimeSpan.FromDays(1));

            Radarr.Deletes.Should().Equal((Instance("Radarr").Settings.Url, request.ExternalItemId.Value));
            Proposal().Status.Should().Be(RemovalStatus.Deleted);
            Reload(request.Id).Status.Should().Be(RequestStatus.Removed);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalRemoved}").And.Contain("Removed from the library after a vote");
            Latest(Alice).Buttons.Should().BeEmpty();

            var bobs = PromptOf(Bob);
            bobs.Content.Should().Contain("🗑️ **Removed** · **Dune (2021)** was removed from the library: nobody asked to keep it. It can be requested again any time.");
            bobs.Buttons.Should().BeEmpty("the outcome has no buttons");
            MessagesOf(Alice, "Up for a vote").Should().BeEmpty("their request message carries the outcome, so the vote notice goes");
            State(CardOf(request.Id)).Should().Be(DiscordText.FinalRemoved);
            Line(CardOf(request.Id), "🗑️ Remov").Should().Be("🗑️ Removed by vote");
            AuditTrail.Single(a => a.Action == "removal.deleted").Detail.Should().Contain("not excluded");

            // A later request is a fresh one.
            await PendingMovie(Bob, Dune);
            RequestStore.All().Where(r => r.TmdbId == Dune).Select(r => r.Status).Should().BeEquivalentTo(new[] { RequestStatus.Removed, RequestStatus.Pending });
        }

        [Test]
        public async Task one_keep_keeps_it_and_nobody_is_told_who_pressed_it()
        {
            await AddCarol();
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);

            var kept = await Keep(Bob);
            kept.Responder.Replies.First().Content.Should().Contain("You asked to keep it");
            kept.Responder.Replies.First().Buttons.Select(b => b.Label).Should().Equal(DiscordText.RemovalTakeBackLabel);
            kept.LastReply.Content.Should().Be(DiscordText.RemovalKeepNoted);
            Line(CardOf(request.Id), "🗑️ Remov").Should().EndWith("1 keep");

            await Tick(TimeSpan.FromDays(7));

            Radarr.Deletes.Should().BeEmpty();
            Proposal().Status.Should().Be(RemovalStatus.Flagged);
            Reload(request.Id).Status.Should().Be(RequestStatus.Available, "kept");

            foreach (var user in new[] { Bob, Carol })
            {
                PromptOf(user).Content.Should().Contain("✅ **Kept** · **Dune (2021)** stays: someone still watches it.").And.NotContain("bob");
                PromptOf(user).Buttons.Should().BeEmpty();
            }

            MessagesOf(Alice, "**Kept**").Single().Content.Should().NotContain("bob", "the requester isn't told who either");
            Line(CardOf(request.Id), "🗑️ Remov").Should().Be("🗑️ Removal: kept by 1 keep (Force or drop)");
        }

        [Test]
        public async Task a_keep_can_be_taken_back_while_the_vote_is_open()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);

            await Keep(Bob);
            var back = await TakeBack(Bob);
            back.Responder.Replies.First().Buttons.Select(b => b.Label).Should().Equal(DiscordText.RemovalKeepLabel);
            back.LastReply.Content.Should().Be(DiscordText.RemovalKeepTakenBack);

            await Tick(TimeSpan.FromDays(7));

            Proposal().Status.Should().Be(RemovalStatus.Deleted);
            (await Keep(Bob)).LastReply.Content.Should().Be(DiscordText.RemovalVoteClosed(Proposal()), "a late click changes nothing");
        }

        [Test]
        public async Task users_who_gain_access_during_the_vote_are_asked_too_and_users_without_access_never_are()
        {
            await AddCarol(movie: "off");
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);
            PromptOf(Carol).Should().BeNull("no Movies access");

            await SetAccess(Carol, "movie", "approval");
            await Tick(TimeSpan.FromMinutes(1));

            PromptOf(Carol).Content.Should().Contain("Still watching?").And.Contain("Dune (2021)");
            PromptOf(Carol).Buttons.Select(b => b.Label).Should().Equal(DiscordText.RemovalKeepLabel);
            Mocker.Resolve<IRemovalPromptRepository>().ForProposal(1).Select(p => p.UserId).Should().BeEquivalentTo(new[] { UserFor(Bob).Id, UserFor(Carol).Id });

            await Tick(TimeSpan.FromMinutes(1));
            MessagesOf(Carol, "Still watching?").Should().ContainSingle("asked once");
        }

        [Test]
        public async Task the_vote_window_is_a_setting()
        {
            Mocker.Resolve<NzbDrone.Core.Configuration.IConfigService>().RemovalVoteDays = 3;
            var request = await DownloadedMovie();

            await UserStartVote(Alice, request.Id);
            await Tick(TimeSpan.FromDays(3));

            Proposal().Status.Should().Be(RemovalStatus.Deleted);
        }

        private static string[] ButtonIdsOf(Requestarr.Core.Discord.Interactions.InteractionReply card) => card.Buttons.Select(b => b.CustomId).ToArray();
    }
}
