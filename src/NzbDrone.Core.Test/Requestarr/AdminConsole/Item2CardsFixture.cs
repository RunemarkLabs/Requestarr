using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.AvailabilityFeed;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.AdminConsole
{
    // Task 3b: /admin user {member} and /admin request {id or title}, the admin panel's member
    // picker, users list and queue. Each card shows only what applies now and refreshes in place.
    [TestFixture]
    public class Item2CardsFixture : FeedScenario
    {
        private const ulong Carol = 900000000000001004;

        [SetUp]
        public void AnotherMember()
        {
            Discord.AddMember(Carol, "carol");
        }

        private static string[] Labels(InteractionReply reply) => reply.Buttons.Select(b => b.Label).ToArray();

        private static string[] Menus(InteractionReply reply) => new[] { reply.SelectMenu }.Concat(reply.MoreMenus).Where(m => m != null).Select(m => m.CustomId.Split(':')[0]).ToArray();

        // Member cards, per member state.
        [Test]
        public async Task someone_not_added_gets_only_add()
        {
            var card = (await MemberCard(Carol)).LastReply;

            card.Ephemeral.Should().BeTrue("only the admin sees it");
            card.Content.Should().Be(DiscordText.MemberNotAdded("carol", Carol));
            card.Buttons.Select(b => b.CustomId).Should().Equal($"adm-add:{Carol}");
            card.SelectMenu.Should().BeNull();

            var added = await MemberButton(Carol, AdminRoutes.Add);
            added.LastReply.Content.Should().StartWith("Added carol.");
            UserFor(Carol).Status.Should().Be(UserStatus.Active);
            Labels(await CardNow(Carol)).Should().Contain(DiscordText.SuspendLabel);
        }

        [Test]
        public async Task an_active_member_gets_one_access_menu_per_type_more_and_actions()
        {
            var card = (await MemberCard(Alice)).LastReply;

            card.Content.Should().Contain("Status: active").And.Contain("Movies: needs approval");
            var menus = new[] { card.SelectMenu }.Concat(card.MoreMenus).ToList();
            menus.Select(m => m.CustomId).Should().Equal($"adm-access:{Alice}:movie", $"adm-access:{Alice}:tv", $"adm-access:{Alice}:music", $"adm-more:{Alice}");
            menus.Take(3).Select(m => m.Placeholder).Should().Equal(DiscordText.AccessSelectPlaceholder(ContentType.Movie), DiscordText.AccessSelectPlaceholder(ContentType.Tv), DiscordText.AccessSelectPlaceholder(ContentType.Music));
            menus[0].Options.Select(o => (o.Value, o.Default)).Should().Equal(("off", false), ("approval", true), ("auto", false));
            menus[2].Options.Single(o => o.Default).Value.Should().Be("off", "Music starts off, and every type is shown to the admin");
            menus[3].Options.Select(o => o.Value).Should().Equal("limit:movie", "limit:tv", "limit:music", $"instance:movie:{Instance("Radarr").Id}", $"instance:movie:{Instance("Radarr 4K").Id}", "requests");
            Labels(card).Should().Equal(DiscordText.AdminLinkPlexLabel, DiscordText.SuspendLabel, DiscordText.RemoveLabel);
            (menus.Count + 1).Should().BeLessThanOrEqualTo(5, "Discord allows five rows");
        }

        [Test]
        public async Task a_suspended_member_gets_unsuspend_instead()
        {
            await MemberButton(Alice, AdminRoutes.Suspend);

            var card = await CardNow(Alice);
            card.Content.Should().Contain("Status: suspended");
            Labels(card).Should().Contain(DiscordText.UnsuspendLabel).And.NotContain(DiscordText.SuspendLabel);
        }

        [Test]
        public async Task a_pending_plex_link_shows_on_the_card_and_deciding_it_refreshes_the_card()
        {
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));
            var card = await CardNow(Alice);
            card.Content.Should().Contain(DiscordText.PendingLink(MediaServerProvider.Plex, "alice_plex"));

            var run = await MemberButton(Alice, AdminRoutes.Link, "1:a");

            run.Responder.Replies.First().Content.Should().Contain("Plex `alice_plex`", "the card is refreshed with the link");
            Labels(run.Responder.Replies.First()).Should().Contain(DiscordText.ChangePlexLabel).And.NotContain(DiscordText.ApproveLinkLabel);
            run.LastReply.Ephemeral.Should().BeTrue();
        }

        [Test]
        public async Task a_removed_member_can_be_added_again()
        {
            await MemberButton(Alice, AdminRoutes.Remove);
            await MemberButton(Alice, AdminRoutes.RemoveConfirm);

            var card = await CardNow(Alice);
            card.Content.Should().Contain("Status: removed").And.Contain(DiscordText.MemberNotAdded("alice", Alice));
            card.Buttons.Select(b => b.CustomId).Should().Equal($"adm-add:{Alice}");
        }

        [Test]
        public async Task remove_asks_first_and_keep_goes_back()
        {
            var ask = (await MemberButton(Alice, AdminRoutes.Remove)).LastReply;
            ask.Content.Should().Contain(DiscordText.ConfirmRemove("alice"));
            Labels(ask).Should().Equal(DiscordText.ConfirmRemoveLabel, DiscordText.KeepLabel);

            var back = (await MemberButton(Alice, AdminRoutes.Refresh)).LastReply;
            Labels(back).Should().Contain(DiscordText.RemoveLabel);
            UserFor(Alice).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task an_access_menu_changes_the_card_in_place_with_nothing_else_posted()
        {
            var access = await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu($"adm-access:{Alice}:tv", "auto")));

            access.Responder.Responses.Select(r => r.Kind).Should().Equal(ResponseKind.MessageUpdate);
            access.LastReply.Content.Should().Contain("TV: auto-approve");
            access.LastReply.MoreMenus.First().Options.Single(o => o.Default).Value.Should().Be("auto", "the menu shows the new level");
        }

        [Test]
        public async Task granting_a_type_with_nothing_connected_tells_the_admin_privately()
        {
            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu($"adm-access:{Alice}:music", "auto")));

            run.Responder.Replies.First().Content.Should().Contain("Music: auto-approve");
            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().Be(DiscordText.NotConnectedYet("alice", ContentType.Music));
            Mocker.Resolve<Requestarr.Core.Discord.IPanelService>().UserPanel(UserFor(Alice)).Content.Should().NotContain("Music", "users don't see a type until an instance for it has connected");
        }

        [Test]
        public async Task the_more_menu_sets_limits_and_instances_and_lists_their_requests()
        {
            var form = await MemberMenu(Alice, AdminRoutes.More, "limit:movie");
            form.Responder.LastModal.CustomId.Should().Be($"adm-limit-form:{Alice}:movie");
            var limit = await MemberForm($"adm-limit-form:{Alice}:movie", ("limit", "2"), ("days", "30"));
            limit.Responder.Replies.First().Content.Should().Contain("Movies: needs approval (quota 2 per 30 days)");

            var bad = await MemberForm($"adm-limit-form:{Alice}:movie", ("limit", "lots"));
            bad.LastReply.Content.Should().Be(DiscordText.InvalidLimit);

            var instance = await MemberMenu(Alice, AdminRoutes.More, $"instance:movie:{Instance("Radarr 4K").Id}");
            instance.Responder.Responses.Select(r => r.Kind).Should().Equal(ResponseKind.MessageUpdate);
            instance.LastReply.Content.Should().Contain(DiscordText.InstanceLine(ContentType.Movie, "Radarr 4K"));

            var plex = await MemberButton(Alice, AdminRoutes.Plex);
            plex.Responder.LastModal.CustomId.Should().Be($"adm-plex-form:{Alice}");
            (await MemberForm($"adm-plex-form:{Alice}", ("username", "alice_plex"))).Responder.Replies.First().Content.Should().Contain("Plex `alice_plex`");
        }

        [Test]
        public async Task their_requests_lists_and_opens_a_members_requests()
        {
            await PendingMovie(Alice, Dune);
            await PendingMovie(Bob, DuneTwo);

            var list = (await MemberMenu(Alice, AdminRoutes.More, "requests")).LastReply;

            list.Content.Should().StartWith(DiscordText.MemberRequestsHeader("alice", 1)).And.Contain("#1 ·").And.NotContain("#2 ·");
            list.SelectMenu.Options.Select(o => o.Value).Should().Equal("1");
        }

        [Test]
        public async Task a_request_card_from_their_requests_acts_in_place()
        {
            await PendingMovie(Alice, Dune);
            var card = (await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu(AdminRoutes.Request, "1")))).LastReply;
            ButtonIds(card).Should().Equal("req-approve:1", "req-deny:1", "req-available:1", "req-admin-cancel:1");
            ButtonIds(card).Should().Equal(ButtonIds(CardOf(1)), "the same buttons as its feed card");

            var approved = await RequestCardButton("req-approve:1");

            approved.Responder.Replies.First().Embeds.Single().Description.Should().StartWith("✅ **Sent**, searching", "the card changes in place");
            approved.LastReply.Content.Should().StartWith("Approved request #1");
            Reload(1).Status.Should().Be(RequestStatus.Approved);

            (await RequestCardButton("req-approve:1")).LastReply.Content.Should().Be(DiscordText.NotPending(1, RequestStatus.Approved));
        }

        // The admin panel.
        [Test]
        public async Task the_member_picker_opens_any_members_card()
        {
            var picker = (await Harness.SendAsync(AsAdmin(FakeInteraction.Button(AdminRoutes.Pick)))).LastReply;

            picker.Ephemeral.Should().BeTrue();
            picker.SelectMenu.Kind.Should().Be(SelectMenuKind.User, "Discord's own member picker");
            picker.SelectMenu.CustomId.Should().Be(AdminRoutes.Member);

            var card = (await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu(AdminRoutes.Member, Carol.ToString())))).LastReply;
            card.Content.Should().Be(DiscordText.MemberNotAdded("carol", Carol));
        }

        [Test]
        public async Task the_users_list_opens_the_same_card()
        {
            var list = (await Harness.SendAsync(AsAdmin(FakeInteraction.Button(PanelRoutes.AdminUsers)))).LastReply;

            list.Content.Should().StartWith(DiscordText.UsersHeader(2));
            list.SelectMenu.Options.Select(o => (o.Label, o.Value)).Should().Equal(("alice", Alice.ToString()), ("bob", Bob.ToString()));
            list.Buttons.Single().CustomId.Should().Be(AdminRoutes.Pick);

            var card = (await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu(AdminRoutes.Member, Bob.ToString())))).LastReply;
            card.Content.Should().StartWith("**bob**");
        }

        // Permissions: the card's custom IDs never carry authority.
        [Test]
        public async Task cards_and_their_buttons_are_admin_only_and_admin_channel_only()
        {
            (await Harness.SendAsync(FakeInteraction.SlashCommand("admin user").WithOption("member", Bob).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.Button($"adm-suspend:{Bob}").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.Button($"adm-suspend:{Bob}").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).LastReply.Content.Should().Be(DiscordText.AdminChannelOnly);
            UserFor(Bob).Status.Should().Be(UserStatus.Active);

            (await Harness.SendAsync(FakeInteraction.Button($"adm-suspend:{Bob}").By(Moderator, "mod").WithRoles(AdminRole).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).LastReply.Content.Should().Be(DiscordText.UserSuspendedDone("bob"));
        }

        private async Task<InteractionReply> CardNow(ulong member) => (await MemberCard(member)).LastReply;
    }
}
