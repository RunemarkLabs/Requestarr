using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Gaps;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.AdminConsole
{
    // Task 3b: each of the 16 admin commands that were removed, done the way it is done now
    // (member card, request card, admin panel), with its effect checked. Nothing was lost.
    [TestFixture]
    public class Item1OldCommandsFixture : GapScenario
    {
        private const ulong Carol = 900000000000001004;

        [SetUp]
        public void AnotherMember()
        {
            Discord.AddMember(Carol, "carol");
        }

        [Test]
        public async Task user_add_is_the_member_cards_add()
        {
            var run = await Harness.SendAsync(AdminCommand("user add", Carol).WithOption("tv", "auto"));

            run.LastReply.Content.Should().StartWith("Added carol.");
            UserFor(Carol).Status.Should().Be(UserStatus.Active);
            Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(UserFor(Carol).Id).Single(a => a.ContentType == ContentType.Tv).State.Should().Be(AccessState.AutoApprove);
        }

        [Test]
        public async Task user_remove_asks_first_on_the_card()
        {
            var ask = await MemberButton(Bob, "adm-remove");
            ask.LastReply.Content.Should().Contain(DiscordText.ConfirmRemove("bob"));
            UserFor(Bob).Status.Should().Be(UserStatus.Active, "nothing happens until it's confirmed");

            await Harness.SendAsync(AdminCommand("user remove", Bob));
            UserFor(Bob).Status.Should().Be(UserStatus.Removed);
        }

        [Test]
        public async Task user_suspend_and_unsuspend_are_card_buttons()
        {
            (await Harness.SendAsync(AdminCommand("user suspend", Bob))).LastReply.Content.Should().Be(DiscordText.UserSuspendedDone("bob"));
            UserFor(Bob).Status.Should().Be(UserStatus.Suspended);

            await Harness.SendAsync(AdminCommand("user unsuspend", Bob));
            UserFor(Bob).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task user_access_with_an_instance_is_the_cards_menus()
        {
            await Harness.SendAsync(AdminCommand("user access", Bob).WithOption("type", "movie").WithOption("level", "auto").WithOption("instance", "Radarr 4K"));

            var row = Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(UserFor(Bob).Id).Single(a => a.ContentType == ContentType.Movie);
            row.State.Should().Be(AccessState.AutoApprove);
            row.BackendId.Should().Be(Instance("Radarr 4K").Id);
        }

        [Test]
        public async Task user_quota_is_the_cards_limit_form()
        {
            await Harness.SendAsync(AdminCommand("user quota", Bob).WithOption("type", "tv").WithOption("limit", 3L).WithOption("days", 14L));

            var row = Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(UserFor(Bob).Id).Single(a => a.ContentType == ContentType.Tv);
            row.QuotaLimit.Should().Be(3);
            row.QuotaWindow.Should().Be(System.TimeSpan.FromDays(14));
        }

        [Test]
        public async Task user_info_is_the_card_itself()
        {
            (await Harness.SendAsync(AdminCommand("user info", Bob))).LastReply.Content.Should().Contain("Status: active").And.Contain($"<#{ChannelOf(Bob)}>");
        }

        [Test]
        public async Task user_mediaserver_is_the_cards_plex_form()
        {
            await Harness.SendAsync(AdminCommand("user mediaserver", Bob).WithOption("service", "plex").WithOption("username", "bob_plex"));

            Mocker.Resolve<IMediaServerAccountRepository>().GetForUser(UserFor(Bob).Id).Single().Username.Should().Be("bob_plex");
        }

        [Test]
        public async Task help_is_still_admin_help()
        {
            (await Admin("help")).LastReply.Content.Should().StartWith("**Requestarr admin help**");
        }

        [Test]
        public async Task queue_is_the_walk_and_a_members_requests_are_on_their_card()
        {
            await PendingMovie(Alice, Dune);
            await AskTv(Bob);

            (await Admin("queue")).LastReply.Content.Should().StartWith(DiscordText.WalkHeader(1, 2)).And.Contain("Dune (2021)");
            var bobs = (await Harness.SendAsync(AdminSlash("queue").WithOption("user", Bob))).LastReply;
            bobs.Content.Should().Contain("#2 ·").And.NotContain("#1 ·");
        }

        [Test]
        public async Task approve_deny_available_and_cancel_are_request_card_buttons()
        {
            await PendingMovie(Alice, Dune);
            await PendingMovie(Bob, DuneTwo);
            await AskTv(Alice);
            await AskTv(Bob, Severance);

            (await Harness.SendAsync(AdminSlash("approve").WithOption("id", 1L))).LastReply.Content.Should().StartWith("Approved request #1");
            (await Harness.SendAsync(AdminSlash("deny").WithOption("id", 2L).WithOption("reason", "no"))).LastReply.Content.Should().Be(DiscordText.AdminDenied(RequestStore.Get(2)));
            (await Harness.SendAsync(AdminSlash("available").WithOption("id", 3L))).LastReply.Content.Should().Be(DiscordText.AdminMarkedAvailable(RequestStore.Get(3)));
            (await Harness.SendAsync(AdminSlash("cancel").WithOption("id", 4L).WithOption("reason", "duplicate"))).LastReply.Content.Should().Be(DiscordText.AdminCancelled(RequestStore.Get(4)));

            RequestStore.All().OrderBy(r => r.Id).Select(r => r.Status).Should().Equal(RequestStatus.Approved, RequestStatus.Denied, RequestStatus.Available, RequestStatus.Cancelled);
            RequestStore.Get(2).DecisionReason.Should().Be("no");
            RequestStore.Get(4).DecisionReason.Should().Be("duplicate");
        }

        [TestCase("mediaserver approve", AccessRequestStatus.Approved)]
        [TestCase("mediaserver deny", AccessRequestStatus.Denied)]
        public async Task mediaserver_approve_and_deny_are_on_the_member_card(string command, AccessRequestStatus status)
        {
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));
            var card = (await MemberCard(Alice)).LastReply;
            card.Content.Should().Contain(DiscordText.PendingLink(MediaServerProvider.Plex, "alice_plex"));
            card.Buttons.Select(b => b.Label).Should().Contain(new[] { DiscordText.ApproveLinkLabel, DiscordText.DenyLinkLabel });

            await Harness.SendAsync(AdminSlash(command).WithOption("id", 1L));

            Mocker.Resolve<IMediaServerAccessRequestRepository>().Get(1).Status.Should().Be(status);
            (await MemberCard(Alice)).LastReply.Buttons.Select(b => b.Label).Should().NotContain(DiscordText.ApproveLinkLabel, "a decided request leaves the card");
        }

        private async Task AskTv(ulong user, int tvdbId = DarkMatter2015)
        {
            var message = await Ask(user, "tv", $"tvdb:{tvdbId}");
            await ClickOn(user, message, $"req-tv-all:{tvdbId}");
        }
    }
}
