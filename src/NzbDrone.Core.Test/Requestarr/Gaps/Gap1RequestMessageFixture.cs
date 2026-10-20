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

            var edited = Message(Alice, message);
            edited.Content.Should().StartWith(DiscordText.FinalApproved).And.Contain("added to Radarr");
            edited.Buttons.Should().BeEmpty();
            edited.Embeds.Single().Color.Should().Be(RequestCards.SentColor);
            ChannelFor(Alice).Messages.Count.Should().Be(before + 1, "the notice that pings the user is still sent");
            LastMessageIn(Alice).Should().Be(DiscordText.RequestApprovedNotice("Dune (2021)", 1, "Radarr"));
        }

        [Test]
        public async Task approve_from_the_command_edits_the_message_too()
        {
            var message = await PendingMovie(Alice, Dune);

            await Harness.SendAsync(AdminSlash("approve").WithOption("id", 1L));

            Message(Alice, message).Content.Should().StartWith(DiscordText.FinalApproved);
            Message(Alice, message).Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task deny_edits_the_message_with_the_reason_and_removes_its_buttons()
        {
            var message = await PendingMovie(Alice, Dune);

            await AdminModal("req-deny-reason:1", RequestRoutes.ReasonField, "not in English");

            var edited = Message(Alice, message);
            edited.Content.Should().StartWith(DiscordText.FinalDenied).And.Contain("not in English");
            edited.Buttons.Should().BeEmpty();
            edited.Embeds.Single().Color.Should().Be(RequestCards.RefusedColor);
            LastMessageIn(Alice).Should().Be(DiscordText.RequestDeniedNotice("Dune (2021)", 1, "not in English"));
        }

        [Test]
        public async Task cancel_from_the_admin_side_edits_the_message_and_the_card()
        {
            var message = await PendingMovie(Alice, Dune);
            var card = ulong.Parse(RequestStore.Get(1).AdminMessageId);

            var run = await Harness.SendAsync(AdminSlash("cancel").WithOption("id", 1L).WithOption("reason", "duplicate"));

            run.LastReply.Content.Should().Be(DiscordText.AdminCancelled(RequestStore.Get(1)));
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Cancelled);
            Message(Alice, message).Content.Should().StartWith(DiscordText.FinalCancelled).And.Contain("cancelled by the admin: duplicate");
            Message(Alice, message).Buttons.Should().BeEmpty();
            LastMessageIn(Alice).Should().Be(DiscordText.RequestCancelledByAdminNotice("Dune (2021)", 1, "duplicate"));

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
            LastMessageIn(Alice).Should().Be(DiscordText.RequestApprovedNotice("Dune (2021)", 1, "Radarr"));
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
        public async Task requests_made_before_messages_were_stored_keep_working()
        {
            await RequestMovie(Alice, Dune);
            RequestStore.Get(1).MessageId.Should().BeNull();

            (await AdminButton("req-approve:1")).LastReply.Content.Should().Contain("approved");
            LastMessageIn(Alice).Should().Be(DiscordText.RequestApprovedNotice("Dune (2021)", 1, "Radarr"));
        }

        private void NowInLibrary(int tmdbId) => Radarr.Catalog.Single(c => c.TmdbId == tmdbId).LibraryId = 55;

        private void ShowsAlreadyAvailable(ulong message)
        {
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Available);
            Message(Alice, message).Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain(DiscordText.RequestAlreadyAvailableStatus("Dune (2021)", 1));
            Message(Alice, message).Buttons.Should().BeEmpty();
            LastMessageIn(Alice).Should().Be(DiscordText.RequestAlreadyAvailableNotice("Dune (2021)", 1));
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
            Message(Alice, message).Content.Should().NotContain(DiscordText.FinalDenied);
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

        [Test]
        public async Task a_tv_request_message_keeps_its_seasons_after_approval()
        {
            var message = await Ask(Alice, "tv", $"tvdb:{DarkMatter}");
            await ClickOn(Alice, message, $"req-tv-all:{DarkMatter}");

            await AdminButton("req-approve:1");

            Message(Alice, message).Content.Should().StartWith(DiscordText.FinalApproved).And.Contain("Seasons: seasons 1–2, plus new seasons as they air.");
            Sonarr.Adds.Single().Seasons.Should().Equal(1, 2);
        }
    }
}
