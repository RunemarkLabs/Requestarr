using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.Gaps
{
    // Gap 1: the request message always shows the request's real state after an admin decides.
    [TestFixture]
    public class Gap1RequestMessageFixture : GapScenario
    {
        [Test]
        public async Task the_request_remembers_its_message()
        {
            var message = await PendingMovie(Alice, Dune);

            RequestStore.Get(1).MessageId.Should().Be(message.ToString());
            RequestStore.Get(1).MessageChannelId.Should().Be(ChannelOf(Alice).ToString());
            RequestStore.Get(1).AdminMessageId.Should().Be(Discord.Channels[AdminChannel].MessageIds.Last().ToString());
            Message(Alice, message).Content.Should().StartWith(DiscordText.FinalPending);
            Message(Alice, message).Buttons.Single().CustomId.Should().Be("req-cancel:1");
        }

        [Test]
        public async Task approve_from_the_card_edits_the_message_and_removes_its_buttons()
        {
            var message = await PendingMovie(Alice, Dune);
            var before = ChannelFor(Alice).Messages.Count;

            await AdminButton("req-approve:1");

            var edited = Latest(Alice);
            edited.Content.Should().StartWith($"<@{Alice}> " + DiscordText.FinalApproved).And.Contain("added to Radarr");
            edited.Buttons.Should().BeEmpty();
            edited.Embeds.Single().Color.Should().Be(RequestCards.SentColor);
            ChannelFor(Alice).Messages.Count.Should().Be(before, "the edited message says it all; no separate notice repeats it");
        }

        [Test]
        public async Task approve_from_the_command_edits_the_message_too()
        {
            var message = await PendingMovie(Alice, Dune);

            await Harness.SendAsync(AdminSlash("approve").WithOption("id", 1L));

            Latest(Alice).Content.Should().StartWith($"<@{Alice}> " + DiscordText.FinalApproved);
            Latest(Alice).Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task deny_edits_the_message_with_the_reason_and_removes_its_buttons()
        {
            var message = await PendingMovie(Alice, Dune);

            await AdminModal("req-deny-reason:1", RequestRoutes.ReasonField, "not in English");

            var edited = Latest(Alice);
            edited.Content.Should().StartWith($"<@{Alice}> " + DiscordText.FinalDenied).And.Contain("not in English");
            edited.Buttons.Should().BeEmpty();
            edited.Embeds.Single().Color.Should().Be(RequestCards.RefusedColor);
            NoSeparateNotice();
        }

        [Test]
        public async Task cancel_from_the_admin_side_edits_the_message_and_the_card()
        {
            var message = await PendingMovie(Alice, Dune);
            var card = ulong.Parse(RequestStore.Get(1).AdminMessageId);

            var run = await Harness.SendAsync(AdminSlash("cancel").WithOption("id", 1L).WithOption("reason", "duplicate"));

            run.LastReply.Content.Should().Be(DiscordText.AdminCancelled(RequestStore.Get(1)));
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Cancelled);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> " + DiscordText.FinalCancelled).And.Contain("cancelled by the admin: duplicate");
            Latest(Alice).Buttons.Should().BeEmpty();
            NoSeparateNotice();

            var adminCard = Discord.Channels[AdminChannel].Messages[Discord.Channels[AdminChannel].MessageIds.IndexOf(card)];
            adminCard.Content.Should().StartWith("**Request #1: cancelled** by owner");
            adminCard.Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task cancelling_from_another_card_also_updates_the_original_message()
        {
            var original = await PendingMovie(Alice, Dune);
            var later = await Ask(Alice, "movie", $"tmdb:{Dune}");

            await ClickOn(Alice, later, "req-cancel:1");

            Message(Alice, later).Content.Should().StartWith(DiscordText.FinalCancelled);
            Message(Alice, original).Content.Should().StartWith(DiscordText.FinalCancelled);
            Message(Alice, original).Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task approval_cards_lose_their_buttons_once_decided()
        {
            await PendingMovie(Alice, Dune);
            var card = ulong.Parse(RequestStore.Get(1).AdminMessageId);

            await Harness.SendAsync(AdminSlash("approve").WithOption("id", 1L));

            var adminCard = Discord.Channels[AdminChannel].Messages[Discord.Channels[AdminChannel].MessageIds.IndexOf(card)];
            adminCard.Content.Should().StartWith("**Request #1: approved** by owner");
            adminCard.Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task a_message_that_is_gone_falls_back_to_the_notice_without_error()
        {
            var message = await PendingMovie(Alice, Dune);
            Discord.DeleteMessage(ChannelOf(Alice), message);

            var run = await AdminButton("req-approve:1");

            run.LastReply.Content.Should().Contain("approved");
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Approved);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalApproved}");
            ExceptionVerification.AssertNoUnexpectedLogs();
        }

        [Test]
        public async Task a_channel_that_is_gone_falls_back_to_the_notice_without_error()
        {
            await PendingMovie(Alice, Dune);
            var request = RequestStore.Get(1);
            request.MessageChannelId = "123456789";
            RequestStore.Update(request);

            (await AdminModal("req-deny-reason:1", RequestRoutes.ReasonField, "no")).LastReply.Content.Should().Contain("denied");
            LastMessageIn(Alice).Should().Be(DiscordText.RequestDeniedNotice("Dune (2021)", 1, "no"));
        }

        [Test]
        public async Task without_a_message_to_edit_the_notice_is_sent_instead()
        {
            await RequestMovie(Alice, Dune);
            NowInLibrary(Dune);

            await AdminButton("req-available:1");

            LastMessageIn(Alice).Should().Be(DiscordText.RequestAlreadyAvailableNotice("Dune (2021)", 1));
        }

        [Test]
        public async Task requests_made_before_messages_were_stored_keep_working()
        {
            await RequestMovie(Alice, Dune);
            RequestStore.Get(1).MessageId.Should().BeNull();

            (await AdminButton("req-approve:1")).LastReply.Content.Should().Contain("approved");
            LastMessageIn(Alice).Should().Be(DiscordText.RequestApprovedNotice("Dune (2021)", 1, "Radarr"));
        }

        private void NowInLibrary(int tmdbId) => Radarr.Catalog.Single(c => c.TmdbId == tmdbId).LibraryId = 55;

        // The request message is the last thing in the channel: nothing was posted after it.
        // After a decision the request's message is the newest in the channel (reposted), and it is the
        // request's tracked message: nothing else repeats it.
        private void NoSeparateNotice()
        {
            ChannelFor(Alice).MessageIds.Last().ToString().Should().Be(RequestStore.Get(1).MessageId);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}>");
        }

        private void ShowsAlreadyAvailable(ulong message)
        {
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Available);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> " + DiscordText.FinalAvailable).And.Contain(DiscordText.RequestAlreadyAvailableStatus("Dune (2021)", 1));
            Latest(Alice).Buttons.Should().BeEmpty();
            NoSeparateNotice();
            Radarr.Adds.Should().BeEmpty("a title already in the library is never added again");
        }

        [Test]
        public async Task denying_a_title_that_is_already_in_the_library_ends_as_already_available()
        {
            var message = await PendingMovie(Alice, Dune);
            NowInLibrary(Dune);

            var run = await AdminModal("req-deny-reason:1", RequestRoutes.ReasonField, "already have it");

            ShowsAlreadyAvailable(message);
            run.LastReply.Content.Should().StartWith("**Request #1: already available**");
            Latest(Alice).Content.Should().NotContain(DiscordText.FinalDenied);
        }

        [Test]
        public async Task approving_a_title_that_is_already_in_the_library_ends_as_already_available()
        {
            var message = await PendingMovie(Alice, Dune);
            NowInLibrary(Dune);

            await AdminButton("req-approve:1");

            ShowsAlreadyAvailable(message);
        }

        [Test]
        public async Task an_admin_cancel_of_a_title_already_in_the_library_ends_as_already_available()
        {
            var message = await PendingMovie(Alice, Dune);
            NowInLibrary(Dune);

            (await Harness.SendAsync(AdminSlash("cancel").WithOption("id", 1L))).LastReply.Content.Should().Be(DiscordText.AdminMarkedAvailable(RequestStore.Get(1)));

            ShowsAlreadyAvailable(message);
        }

        [Test]
        public async Task the_already_available_button_closes_a_request_the_admin_knows_is_covered()
        {
            var message = await PendingMovie(Alice, Dune);
            var card = Discord.Channels[AdminChannel].Messages.Last();
            card.Buttons.Select(b => b.Label).Should().Equal(DiscordText.ApproveLabel, DiscordText.DenyLabel, DiscordText.AlreadyAvailableLabel);

            var run = await AdminButton("req-available:1");

            ShowsAlreadyAvailable(message);
            run.Responder.Replies.First().Content.Should().StartWith("**Request #1: already available**").And.Contain("closed by owner");
            run.Responder.Replies.First().Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task the_available_command_does_the_same_and_refuses_decided_requests()
        {
            var message = await PendingMovie(Alice, Dune);

            (await Harness.SendAsync(AdminSlash("available").WithOption("id", 1L))).LastReply.Content.Should().Be(DiscordText.AdminMarkedAvailable(RequestStore.Get(1)));
            ShowsAlreadyAvailable(message);

            (await Harness.SendAsync(AdminSlash("available").WithOption("id", 1L))).LastReply.Content.Should().Be(DiscordText.NotPending(1, RequestStatus.Available));
        }

        private static void LooksLikeAWin(Requestarr.Core.Discord.Interactions.InteractionReply reply)
        {
            System.Text.RegularExpressions.Regex.Replace(reply.Content, @"^<@\d+> ", string.Empty).Should().StartWith(DiscordText.FinalAvailable).And.Contain("Good news");
            reply.Content.Should().NotContainAny("⛔", "🚫", "⚠️", "Denied", "Cancelled", "Not requested", "Couldn't");
            reply.Embeds.Single().Color.Should().Be(RequestCards.SentColor);
        }

        [Test]
        public async Task requesting_a_title_already_in_the_library_is_a_win()
        {
            var final = (await RequestMovie(Alice, Arrival)).LastReply;

            LooksLikeAWin(final);
            final.Content.Should().Contain(DiscordText.AlreadyInLibrary);
            final.Ephemeral.Should().BeFalse();
        }

        [Test]
        public async Task the_card_of_a_title_in_the_library_brings_good_news()
        {
            var card = (await Harness.SendAsync(Search(Alice, "movie", $"tmdb:{Arrival}"))).LastReply;

            card.Content.Should().Contain(DiscordText.StateInLibrary).And.Contain("Good news");
        }

        [Test]
        public async Task an_admin_cancel_of_a_title_now_in_the_library_tells_the_user_good_news()
        {
            var message = await PendingMovie(Alice, Dune);
            NowInLibrary(Dune);

            await Harness.SendAsync(AdminSlash("cancel").WithOption("id", 1L).WithOption("reason", "we have it"));

            LooksLikeAWin(Latest(Alice));
            NoSeparateNotice();
        }

        [Test]
        public async Task an_add_that_finds_the_title_already_there_is_a_win_not_a_failure()
        {
            var message = await PendingMovie(Alice, Dune);
            Radarr.AlreadyAdded = true;

            await AdminButton("req-approve:1");

            RequestStore.Get(1).Status.Should().Be(RequestStatus.Available);
            LooksLikeAWin(Latest(Alice));
            NoSeparateNotice();
        }

        [Test]
        public async Task an_auto_approved_request_that_finds_the_title_already_there_is_a_win()
        {
            await SetAccess(Alice, "movie", "auto");
            Radarr.AlreadyAdded = true;

            var final = (await RequestMovie(Alice, Dune)).LastReply;

            LooksLikeAWin(final);
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Available);
        }

        [Test]
        public async Task a_late_click_on_a_title_now_in_the_library_gets_the_good_news()
        {
            NowInLibrary(Dune);

            var run = await Harness.SendAsync(Requestarr.Core.Test.Discord.Harness.FakeInteraction.Button($"req-follow:movie:{Dune}").By(Alice).InGuild(Requestarr.Core.Test.Discord.Harness.FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.AlreadyThereNote);
        }

        [Test]
        public async Task a_tv_request_message_keeps_its_seasons_after_approval()
        {
            var message = await Ask(Alice, "tv", $"tvdb:{DarkMatter}");
            await ClickOn(Alice, message, $"req-tv-all:{DarkMatter}");

            await AdminButton("req-approve:1");

            Latest(Alice).Content.Should().StartWith($"<@{Alice}> " + DiscordText.FinalApproved).And.Contain("Seasons: seasons 1–2, plus new seasons as they air.");
            Sonarr.Adds.Single().Seasons.Should().Equal(1, 2);
        }
    }
}
