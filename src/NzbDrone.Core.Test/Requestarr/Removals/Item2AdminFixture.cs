using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Removals
{
    // Task 4 items 2, 3 and 11: the admin starts a removal from the feed card's Delete; a requested title
    // asks its requester first (Yes starts the vote, No keeps it, silence counts as yes after the
    // timeout with a reminder on day 5); Force works at every stage; removals wait in /admin requests.
    [TestFixture]
    public class Item2AdminFixture : RemovalScenario
    {
        [Test]
        public async Task delete_on_the_feed_card_asks_the_requester_first_and_says_silence_counts_as_yes()
        {
            var request = await DownloadedMovie();
            ButtonIdsOf(CardOf(request.Id)).Should().Equal($"rm-feed:{request.Id}");

            var card = (await FeedClick(request.Id, $"{RemovalRoutes.Feed}:{request.Id}")).LastReply;
            card.Ephemeral.Should().BeTrue();
            card.Content.Should().Contain("**Remove **Dune (2021)**** · Radarr").And.Contain("Requested by **alice**: they're asked first");
            card.Buttons.Select(b => b.Label).Should().Equal(DiscordText.StartRemovalLabel, DiscordText.ForceNowLabel);

            var started = (await Press(card, DiscordText.StartRemovalLabel)).LastReply;
            started.Content.Should().StartWith("Asked alice first");

            var ask = Latest(Alice);
            ask.Content.Should().StartWith($"<@{Alice}> 🗑️ **Remove your request?**").And.Contain("No answer by").And.Contain("counts as yes").And.Contain("you'll get a reminder");
            ask.Buttons.Select(b => b.Label).Should().Equal(DiscordText.RemovalYesLabel, DiscordText.RemovalNoLabel);
            Proposal().Status.Should().Be(RemovalStatus.AwaitingRequester);
            Proposal().AnswerBy.Should().Be(Now.AddDays(7));
            PromptOf(Bob).Should().BeNull("nobody votes before the requester answers");
            Line(CardOf(request.Id), "🗑️ Remov").Should().StartWith("🗑️ Removal: asked alice, answer by");

            var walk = (await Walk()).LastReply;
            walk.Content.Should().Contain("is waiting on **alice**").And.Contain("counts as yes and starts the vote");
            walk.Buttons.Select(b => b.Label).Should().Contain(DiscordText.ForceRemovalLabel).And.Contain(DiscordText.DropRemovalLabel);
        }

        [Test]
        public async Task the_requesters_yes_starts_the_vote_without_asking_them_again()
        {
            var request = await DownloadedMovie();
            await AdminStart(request);

            await Answer(Alice, yes: true);

            Proposal().Status.Should().Be(RemovalStatus.Voting);
            Proposal().RequesterResponse.Should().Be(RequesterResponse.Yes);
            MessagesOf(Alice, "You agreed to remove").Should().ContainSingle();
            PromptOf(Alice).Should().BeNull("they said yes");
            PromptOf(Bob).Content.Should().Contain("Still watching?");
            (await Answer(Alice, yes: false)).LastReply.Content.Should().Be(DiscordText.RemovalAlreadyMoved(Proposal()));
            (await Answer(Bob, yes: true)).LastReply.Content.Should().Be(DiscordText.RemovalNotYours);
        }

        [Test]
        public async Task the_requesters_no_keeps_it_and_waits_on_the_admin_who_can_drop_or_force()
        {
            var request = await DownloadedMovie();
            await AdminStart(request);

            await Answer(Alice, yes: false);

            Proposal().Status.Should().Be(RemovalStatus.Declined);
            MessagesOf(Alice, "You chose to keep").Should().ContainSingle();
            PromptOf(Bob).Should().BeNull();
            Line(CardOf(request.Id), "🗑️ Remov").Should().Be("🗑️ Removal: alice said keep (Force or drop)");

            var walk = (await Walk()).LastReply;
            walk.Content.Should().Contain("**alice** said keep **Dune (2021)**");
            var dropped = (await Press(walk, DiscordText.DropRemovalLabel)).LastReply;
            dropped.Content.Should().Contain("Dropped").And.EndWith(DiscordText.WalkDone);
            Proposal().Status.Should().Be(RemovalStatus.Cancelled);
            Radarr.Deletes.Should().BeEmpty();

            // Re-issued with Force: deleted now, audited with who and when.
            var forced = await AdminStart(request, force: true);
            forced.LastReply.Content.Should().StartWith("Removed **Dune (2021)** from Radarr with its files").And.Contain("can be requested again");
            Radarr.Deletes.Should().ContainSingle();
            Proposal(2).Status.Should().Be(RemovalStatus.Forced);
            Proposal(2).ClosedByName.Should().Be("owner");
            Reload(request.Id).Status.Should().Be(RequestStatus.Removed);
            Latest(Alice).Content.Should().Contain("Removed from the library by the admin");
            AuditTrail.Single(a => a.Action == "removal.deleted").Detail.Should().Contain("forced by owner");
        }

        [Test]
        public async Task no_answer_by_day_seven_counts_as_yes_with_a_reminder_on_day_five()
        {
            var request = await DownloadedMovie();
            await AdminStart(request);

            await Tick(TimeSpan.FromDays(4));
            MessagesOf(Alice, "Reminder").Should().BeEmpty();
            await Tick(TimeSpan.FromDays(1));
            var reminder = Latest(Alice);
            reminder.Content.Should().StartWith($"<@{Alice}> ⏰ **Reminder**").And.Contain("counts as yes");
            reminder.Buttons.Select(b => b.Label).Should().Equal(DiscordText.RemovalYesLabel, DiscordText.RemovalNoLabel);
            MessagesOf(Alice, "Remove your request?").Should().BeEmpty("the reminder replaces the first message");
            await Tick(TimeSpan.FromDays(1));
            MessagesOf(Alice, "Reminder").Should().ContainSingle("reminded once");

            await Tick(TimeSpan.FromDays(1));

            Proposal().Status.Should().Be(RemovalStatus.Voting);
            Proposal().RequesterResponse.Should().Be(RequesterResponse.Silent);
            MessagesOf(Alice, "No answer came").Should().ContainSingle();
            PromptOf(Alice).Buttons.Select(b => b.Label).Should().Equal(new[] { DiscordText.RemovalKeepLabel }, "silent requesters can still keep it in the vote");
            PromptOf(Bob).Should().NotBeNull();

            await Keep(Alice);
            await Tick(TimeSpan.FromDays(7));
            Proposal().Status.Should().Be(RemovalStatus.Flagged);
        }

        [Test]
        public async Task the_answer_window_and_reminder_day_are_settings()
        {
            var config = Mocker.Resolve<NzbDrone.Core.Configuration.IConfigService>();
            config.RemovalRequesterTimeoutDays = 3;
            config.RemovalReminderDay = 2;
            var request = await DownloadedMovie();
            await AdminStart(request);

            await Tick(TimeSpan.FromDays(2));
            MessagesOf(Alice, "Reminder").Should().ContainSingle();
            await Tick(TimeSpan.FromDays(1));
            Proposal().Status.Should().Be(RemovalStatus.Voting);
        }

        [Test]
        public async Task force_skips_the_vote_or_overrides_keeps_at_any_stage()
        {
            // While the vote is open: the feed card's Force asks first.
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);
            await Keep(Bob);
            var confirm = (await FeedClick(request.Id, $"{RemovalRoutes.Force}:1")).LastReply;
            confirm.Content.Should().Contain("**now**").And.Contain("can't be undone");
            Radarr.Deletes.Should().BeEmpty("asking first deletes nothing");

            var done = await Harness.SendAsync(AsAdmin(FakeInteraction.Button(confirm.Buttons.Single().CustomId)));

            done.LastReply.Content.Should().StartWith("Removed **Dune (2021)**");
            Proposal().Status.Should().Be(RemovalStatus.Forced);
            PromptOf(Bob).Content.Should().Contain("🗑️ **Removed by the admin** · **Dune (2021)** was removed from the library.");
            Line(CardOf(request.Id), "🗑️ Remov").Should().Be("🗑️ Removed by owner (forced)");
            AuditTrail.Single(a => a.Action == "removal.force").Detail.Should().Contain("forced by owner at").And.Contain("1 keep flag(s)");
        }

        [Test]
        public async Task a_vote_kept_by_keeps_shows_the_admin_who_pressed_it_and_can_be_forced_from_the_walk()
        {
            await AddCarol();
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);
            await Keep(Carol);
            await Tick(TimeSpan.FromDays(7));

            var walk = (await Walk()).LastReply;
            walk.Content.Should().Contain("The vote kept **Dune (2021)**: 1 keep (carol)");
            var confirm = (await Press(walk, DiscordText.ForceRemovalLabel)).LastReply;
            confirm.Content.Should().Contain("**now**");
            var done = (await Press(confirm, DiscordText.ConfirmDeleteLabel)).LastReply;

            done.Content.Should().Contain("Removed **Dune (2021)** from Radarr").And.EndWith(DiscordText.WalkDone);
            Radarr.Deletes.Should().ContainSingle();
            PromptOf(Carol).Content.Should().Contain("Removed by the admin");
        }

        [Test]
        public async Task dropping_a_kept_vote_closes_it_as_kept()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);
            await Keep(Bob);
            await Tick(TimeSpan.FromDays(7));
            var told = PromptOf(Bob).Content;

            await FeedClick(request.Id, $"{RemovalRoutes.Drop}:1");

            Proposal().Status.Should().Be(RemovalStatus.Kept);
            PromptOf(Bob).Content.Should().Be(told, "users were already told it's kept");
            Line(CardOf(request.Id), "🗑️ Remov").Should().Be("🗑️ Removal: kept (1 keep)");
            ButtonIdsOf(CardOf(request.Id)).Should().Equal(new[] { $"rm-feed:{request.Id}" }, "a new removal can be started later");
        }

        [Test]
        public async Task cancelling_an_open_vote_tells_everyone_it_was_called_off()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);

            await FeedClick(request.Id, $"{RemovalRoutes.Drop}:1");

            Proposal().Status.Should().Be(RemovalStatus.Cancelled);
            PromptOf(Bob).Content.Should().Contain("✅ **Kept** · The removal of **Dune (2021)** was called off.");
            PromptOf(Bob).Buttons.Should().BeEmpty();
            Line(CardOf(request.Id), "🗑️ Remov").Should().Be("🗑️ Removal cancelled by owner");
        }

        [Test]
        public async Task a_deletion_sonarr_or_radarr_refuses_waits_in_the_walk_and_force_tries_again()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);
            Radarr.FailDeletes = true;

            await Tick(TimeSpan.FromDays(7));

            Proposal().Status.Should().Be(RemovalStatus.Failed);
            Reload(request.Id).Status.Should().Be(RequestStatus.Available, "nothing changed");
            PromptOf(Bob).Content.Should().Contain("Still watching?", "nobody is told an outcome that didn't happen");
            var walk = (await Walk()).LastReply;
            walk.Content.Should().Contain("Removing **Dune (2021)** failed: 500 InternalServerError: disk is read-only");

            Radarr.FailDeletes = false;
            var confirm = (await Press(walk, DiscordText.ForceRemovalLabel)).LastReply;
            await Press(confirm, DiscordText.ConfirmDeleteLabel);

            Proposal().Status.Should().Be(RemovalStatus.Forced);
            Radarr.Deletes.Should().ContainSingle();
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task removal_buttons_are_for_admins_and_votes_for_the_people_asked()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);

            (await Harness.SendAsync(FakeInteraction.Button($"{RemovalRoutes.Force}:1").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.Button(RemovalRoutes.Search).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Keep(Alice)).LastReply.Content.Should().Be(DiscordText.RemovalNotYours, "the requester who asked has no vote");
            Mocker.Resolve<IRemovalFlagRepository>().All().Should().BeEmpty();
        }

        private static string[] ButtonIdsOf(Requestarr.Core.Discord.Interactions.InteractionReply card) => card.Buttons.Select(b => b.CustomId).ToArray();
    }
}
