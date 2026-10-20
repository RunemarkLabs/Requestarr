using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.AvailabilityFeed
{
    // Item 2: the admin-only feed channel, one compact card per request, reposted at the bottom on
    // every change with only the buttons valid now; approval cards and the feed stay in step.
    [TestFixture]
    public class Item2FeedFixture : FeedScenario
    {
        [Test]
        public void the_feed_channel_is_created_next_to_the_admin_channel_for_admins_only()
        {
            var feed = Discord.ChannelNamed(ChannelLayout.FeedChannelName);

            feed.Should().NotBeNull();
            Config.FeedChannelId.Should().Be(feed.Id.ToString());
            feed.CategoryId.Should().Be(Discord.ChannelNamed(ChannelLayout.AdminChannelName).CategoryId);
            feed.Topic.Should().Be(DiscordText.FeedChannelTopic);
            OverwriteFor(feed, FakeDiscordGuild.Guild).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(feed, AdminRole).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(feed, Discord.BotUserId).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(feed, RoleOf(Alice)).Should().BeNull("users never see the feed");
        }

        [Test]
        public async Task the_feed_channel_survives_restarts_and_comes_back_if_deleted()
        {
            var first = FeedChannel;

            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            FeedChannel.Should().Be(first);

            Discord.Channels.Remove(first);
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            FeedChannel.Should().NotBe(first);
            Config.FeedSeeded.Should().BeFalse("a new feed channel is seeded again");
        }

        [Test]
        public async Task a_new_request_gets_a_compact_card_with_the_pending_buttons()
        {
            await PendingMovie(Alice, Dune);

            var card = CardOf(1);
            card.Content.Should().BeNullOrEmpty();
            card.Embeds.Single().Title.Should().Be("Dune (2021)");
            card.Embeds.Single().ThumbnailUrl.Should().Be("https://image.example/dune.jpg");
            card.Embeds.Single().ImageUrl.Should().Be(DiscordText.FeedCardWidthUrl, "a transparent spacer makes every card the same width; the poster stays a small thumbnail");
            System.IO.File.Exists(Requestarr.Core.Test.AnimeDub.Item4To6Fixture.RepoFile("docs", "assets", "feed-card-width.png")).Should().BeTrue();
            card.Embeds.Single().Description.Split('\n').Should().Equal(DiscordText.FinalPending, "🎬 Movie · alice · Radarr");
            ButtonIds(card).Should().Equal("req-approve:1", "req-deny:1", "req-available:1", "req-admin-cancel:1");
        }

        [Test]
        public async Task a_tv_card_shows_the_seasons_and_add_seasons()
        {
            await AskSeasons(Alice, DarkMatter2015, 1, 2);

            var card = CardOf(1);
            card.Embeds.Single().Description.Should().Contain("📺 TV · alice · Sonarr").And.Contain("Seasons: seasons 1–2 only");
            ButtonIds(card).Should().Contain("req-add:1");
        }

        [Test]
        public async Task approving_from_the_feed_reposts_everything_once_and_keeps_the_approval_card_in_step()
        {
            await PendingMovie(Alice, Dune);
            var oldCard = ulong.Parse(Reload(1).FeedMessageId);
            var approvalCard = ulong.Parse(Reload(1).AdminMessageId);

            var run = await FeedClick(1, "req-approve:1");

            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().StartWith("Approved request #1");
            Reload(1).Status.Should().Be(RequestStatus.Approved);
            Feed.MessageIds.Should().NotContain(oldCard);
            Feed.MessageIds.Last().ToString().Should().Be(Reload(1).FeedMessageId, "the new card is at the bottom");
            CardsFor(1).Should().Be(1);
            State(CardOf(1)).Should().Be("✅ **Sent**, searching");
            ButtonIds(CardOf(1)).Should().Equal("req-admin-cancel:1");
            Discord.Channels[AdminChannel].Messages[Discord.Channels[AdminChannel].MessageIds.IndexOf(approvalCard)].Content.Should().StartWith("**Request #1: approved**");
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalApproved}");
        }

        [Test]
        public async Task approving_from_the_approval_card_reposts_the_feed_card()
        {
            await PendingMovie(Alice, Dune);
            var oldCard = ulong.Parse(Reload(1).FeedMessageId);

            await AdminButton("req-approve:1");

            Feed.MessageIds.Should().NotContain(oldCard);
            State(CardOf(1)).Should().Be("✅ **Sent**, searching");
        }

        [Test]
        public async Task denying_from_the_feed_asks_for_a_reason_and_leaves_a_card_without_buttons()
        {
            await PendingMovie(Alice, Dune);

            var modal = await FeedClick(1, "req-deny:1");
            modal.Responder.LastModal.CustomId.Should().Be("req-deny-reason:1");

            await Harness.SendAsync(FakeInteraction.ModalSubmit("req-deny-reason:1").WithField(RequestRoutes.ReasonField, "not this one").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(FeedChannel));

            State(CardOf(1)).Should().Be($"{DiscordText.FinalDenied}: not this one");
            CardOf(1).Buttons.Should().BeEmpty();
            Discord.Channels[AdminChannel].Messages.Any(m => m.Content?.StartsWith("**Request #1: denied**") == true).Should().BeTrue();
        }

        [Test]
        public async Task already_available_from_the_feed_closes_it_as_good_news()
        {
            await PendingMovie(Alice, Dune);

            await FeedClick(1, "req-available:1");

            State(CardOf(1)).Should().Be(DiscordText.FinalAvailable);
            Latest(Alice).Content.Should().Contain(DiscordText.FinalAvailable);
        }

        [Test]
        public async Task cancelling_a_pending_request_from_the_feed()
        {
            await PendingMovie(Alice, Dune);

            var run = await FeedClick(1, "req-admin-cancel:1");

            Reload(1).Status.Should().Be(RequestStatus.Cancelled);
            run.LastReply.Content.Should().Be(DiscordText.AdminCancelled(Reload(1)));
            State(CardOf(1)).Should().Be(DiscordText.FinalCancelled);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalCancelled}");
        }

        [Test]
        public async Task cancelling_a_sent_request_stops_the_search_and_says_exactly_what_happened()
        {
            var request = await SentMovie();

            var run = await FeedClick(request.Id, $"req-admin-cancel:{request.Id}");

            Reload(request.Id).Status.Should().Be(RequestStatus.Cancelled);
            Radarr.Monitored[101].Should().BeFalse("it is unmonitored in Radarr, not deleted");
            run.LastReply.Content.Should().Contain("stays in Radarr").And.Contain("no longer monitored").And.Contain("Nothing was deleted");
            AuditTrail.Last(a => a.Action == "request.cancel").Detail.Should().StartWith("unmonitored on Radarr");
        }

        [Test]
        public async Task a_cancel_radarr_cannot_act_on_changes_nothing()
        {
            var request = await SentMovie();
            Radarr.FailMonitor = true;

            var run = await FeedClick(request.Id, $"req-admin-cancel:{request.Id}");

            Reload(request.Id).Status.Should().Be(RequestStatus.Approved);
            run.LastReply.Content.Should().Be(DiscordText.CancelNotStopped(Reload(request.Id), "Radarr"));
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task once_downloading_there_is_no_cancel_and_a_late_cancel_is_refused()
        {
            var request = await SentMovie();
            Radarr.Downloading(101);
            await Availability.CheckAllAsync();

            ButtonIds(CardOf(request.Id)).Should().NotContain($"req-admin-cancel:{request.Id}");

            var run = await FeedClick(request.Id, $"req-admin-cancel:{request.Id}");

            run.LastReply.Content.Should().Be(DiscordText.CannotCancelDownloading(request.Id));
            Reload(request.Id).Status.Should().Be(RequestStatus.Approved);
            CardsFor(request.Id).Should().Be(1);
        }

        [Test]
        public async Task a_late_approve_in_the_feed_is_refused_and_the_card_refreshed()
        {
            await PendingMovie(Alice, Dune);
            await FeedClick(1, "req-approve:1");

            var late = await Harness.SendAsync(FakeInteraction.Button("req-approve:1").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(FeedChannel));

            late.LastReply.Content.Should().Be(DiscordText.NotPending(1, RequestStatus.Approved));
            Radarr.Adds.Should().ContainSingle();
            CardsFor(1).Should().Be(1);
            ButtonIds(CardOf(1)).Should().NotContain("req-approve:1");
        }

        [Test]
        public async Task people_who_are_not_admins_cannot_use_feed_buttons()
        {
            await PendingMovie(Alice, Dune);

            var run = await FeedClick(1, "req-approve:1", by: Bob);

            run.LastReply.Content.Should().Be(DiscordText.AdminOnly);
            Reload(1).Status.Should().Be(RequestStatus.Pending);
        }

        [Test]
        public async Task the_moderator_role_can_act_from_the_feed()
        {
            await PendingMovie(Alice, Dune);

            await Harness.SendAsync(FakeInteraction.Button("req-approve:1").By(Moderator, "mod").WithRoles(AdminRole).InGuild(FakeDiscordGuild.Guild).InChannel(FeedChannel));

            Reload(1).Status.Should().Be(RequestStatus.Approved);
        }

        [Test]
        public async Task a_follower_joining_reposts_the_card_with_who_follows()
        {
            await PendingMovie(Alice, Dune);
            var before = ulong.Parse(Reload(1).FeedMessageId);

            var card = await Ask(Bob, "movie", $"tmdb:{Dune}");
            await ClickOn(Bob, card, $"req-follow:movie:{Dune}");

            Feed.MessageIds.Should().NotContain(before);
            CardOf(1).Embeds.Single().Description.Should().Contain("Following: bob");
            CardsFor(1).Should().Be(1);
        }

        [Test]
        public async Task every_change_keeps_exactly_one_card_per_request_newest_last()
        {
            await PendingMovie(Alice, Dune);
            await PendingMovie(Bob, DuneTwo);
            await FeedClick(1, "req-approve:1");
            Radarr.Downloading(101);
            await Availability.CheckAllAsync();
            Radarr.Imported(101);
            await Availability.CheckAllAsync();

            CardsFor(1).Should().Be(1);
            CardsFor(2).Should().Be(1);
            IsCardOf(LatestCard, 1).Should().BeTrue("request 1 changed last");
            State(CardOf(1)).Should().Be(DiscordText.FinalReady);
            State(CardOf(2)).Should().Be(DiscordText.FinalPending);
        }

        [Test]
        public async Task a_card_that_cannot_be_deleted_loses_its_buttons_and_a_new_one_is_posted()
        {
            await PendingMovie(Alice, Dune);
            var old = ulong.Parse(Reload(1).FeedMessageId);
            Discord.CannotDelete = true;

            await AdminButton("req-approve:1");

            Feed.Messages[Feed.MessageIds.IndexOf(old)].Buttons.Should().BeEmpty();
            Feed.MessageIds.Last().ToString().Should().Be(Reload(1).FeedMessageId);
        }

        [Test]
        public async Task first_start_seeds_cards_for_open_requests_only_once()
        {
            await PendingMovie(Alice, Dune);
            await PendingMovie(Bob, DuneTwo);
            await AdminModal("req-deny-reason:2", RequestRoutes.ReasonField, "no");
            var done = await SentShow(Alice, Severance, 1);
            Sonarr.Season(done.ExternalItemId.Value, 1, aired: 9, withFiles: 9);
            await Availability.CheckAllAsync();
            var downloading = await SentShow(Bob, DarkMatter2015, 1);

            // Requests from before the feed existed: a fresh feed channel, nothing in it.
            Discord.Channels.Remove(FeedChannel);
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            Feed.Messages.Should().BeEmpty();

            await Mocker.Resolve<IRequestFeed>().SeedAsync();
            await Mocker.Resolve<IRequestFeed>().SeedAsync();

            Reload(done.Id).Status.Should().Be(RequestStatus.Available);
            Feed.Messages.Should().HaveCount(2, "only pending or in-progress requests: not the denied or the finished one");
            IsCardOf(Feed.Messages[0], 1).Should().BeTrue();
            IsCardOf(Feed.Messages[1], downloading.Id).Should().BeTrue();
            Config.FeedSeeded.Should().BeTrue();
        }

        [Test]
        public async Task account_events_go_to_the_admin_channel_and_never_the_feed()
        {
            var feedBefore = Feed.Messages.Count;

            await AddUser(Moderator);
            await SetAccess(Alice, "tv", "auto");
            await Harness.SendAsync(AdminCommand("user suspend", Bob));
            await Harness.SendAsync(AdminCommand("user unsuspend", Bob));
            await Harness.SendAsync(PlexForm(Alice, "alice-plex"));
            await Users.HandleMemberLeftAsync(Alice);

            Feed.Messages.Count.Should().Be(feedBefore);
            Discord.Channels[AdminChannel].Messages.Last().Content.Should().Be(DiscordText.MemberLeftAdminNotice("alice"));
        }
    }
}
