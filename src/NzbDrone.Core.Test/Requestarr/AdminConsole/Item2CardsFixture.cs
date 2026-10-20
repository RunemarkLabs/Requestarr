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
        public async Task an_active_member_gets_access_limits_instance_and_actions()
        {
            var card = (await MemberCard(Alice)).LastReply;

            card.Content.Should().Contain("Status: active").And.Contain("Movies: needs approval");
            Menus(card).Should().Equal(AdminRoutes.Access, AdminRoutes.Limit, AdminRoutes.Instance);
            card.SelectMenu.Options.Should().HaveCount(9, "three types, three levels");
            card.SelectMenu.Options.Single(o => o.Value == "movie:approval").Description.Should().Be(DiscordText.Current);
            card.MoreMenus.Single(m => m.CustomId.StartsWith(AdminRoutes.Instance)).Options.Select(o => o.Label)
                .Should().Equal(DiscordText.InstanceOption(ContentType.Movie, "Radarr"), DiscordText.InstanceOption(ContentType.Movie, "Radarr 4K"));
            Labels(card).Should().Equal(DiscordText.AdminLinkPlexLabel, DiscordText.SuspendLabel, DiscordText.RemoveLabel, DiscordText.TheirRequestsLabel);
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
        public async Task every_change_refreshes_the_card_and_reports_privately()
        {
            var access = await MemberMenu(Alice, AdminRoutes.Access, "tv:auto");
            access.Responder.Replies.First().Content.Should().Contain("TV: auto-approve");
            access.LastReply.Ephemeral.Should().BeTrue();
            access.LastReply.Content.Should().Be(DiscordText.AccessChanged("alice", ContentType.Tv, AccessState.AutoApprove, null));

            var form = await MemberMenu(Alice, AdminRoutes.Limit, "movie");
            form.Responder.LastModal.CustomId.Should().Be($"adm-limit-form:{Alice}:movie");
            var limit = await MemberForm($"adm-limit-form:{Alice}:movie", ("limit", "2"), ("days", "30"));
            limit.Responder.Replies.First().Content.Should().Contain("Movies: needs approval (quota 2 per 30 days)");

            var bad = await MemberForm($"adm-limit-form:{Alice}:movie", ("limit", "lots"));
            bad.LastReply.Content.Should().Be(DiscordText.InvalidLimit);

            var plex = await MemberButton(Alice, AdminRoutes.Plex);
            plex.Responder.LastModal.CustomId.Should().Be($"adm-plex-form:{Alice}");
            (await MemberForm($"adm-plex-form:{Alice}", ("username", "alice_plex"))).Responder.Replies.First().Content.Should().Contain("Plex `alice_plex`");

            var instance = await MemberMenu(Alice, AdminRoutes.Instance, $"movie:{Instance("Radarr 4K").Id}");
            instance.Responder.Replies.First().Content.Should().Contain(DiscordText.InstanceLine(ContentType.Movie, "Radarr 4K"));
        }

        [Test]
        public async Task their_requests_lists_and_opens_a_members_requests()
        {
            await PendingMovie(Alice, Dune);
            await PendingMovie(Bob, DuneTwo);

            var list = (await MemberButton(Alice, AdminRoutes.Requests)).LastReply;

            list.Content.Should().StartWith(DiscordText.MemberRequestsHeader("alice", 1)).And.Contain("#1 ·").And.NotContain("#2 ·");
            list.SelectMenu.Options.Select(o => o.Value).Should().Equal("1");
        }

        // Request cards.
        [TestCase("1")]
        [TestCase("#1")]
        [TestCase("dune (2021")]
        public async Task admin_request_opens_the_card_by_number_or_title(string query)
        {
            await PendingMovie(Alice, Dune);

            var card = (await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin request").WithOption("request", query)))).LastReply;

            card.Ephemeral.Should().BeTrue();
            card.Embeds.Single().Title.Should().Be("Dune (2021)");
            ButtonIds(card).Should().Equal(ButtonIds(CardOf(1)), "the same buttons as its feed card");
        }

        [Test]
        public async Task several_matches_give_a_list_and_nothing_found_says_how_to_search()
        {
            await PendingMovie(Alice, Dune);
            await PendingMovie(Bob, DuneTwo);

            var list = (await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin request").WithOption("request", "dune")))).LastReply;
            list.Content.Should().StartWith(DiscordText.RequestMatches("dune", 2));
            list.SelectMenu.CustomId.Should().Be(AdminRoutes.Request);

            var opened = (await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu(AdminRoutes.Request, "2")))).LastReply;
            opened.Embeds.Single().Title.Should().Be("Dune: Part Two (2024)");

            (await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin request").WithOption("request", "nothing like it")))).LastReply.Content.Should().Be(DiscordText.NoRequestMatches);
        }

        [Test]
        public async Task the_title_autocompletes_from_existing_requests()
        {
            await PendingMovie(Alice, Dune);
            await PendingMovie(Bob, DuneTwo);

            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.Autocomplete("admin request", "request", "part")));

            run.Responder.Suggestions.Select(c => (c.Name, c.Value)).Should().Equal(("#2 Dune: Part Two (2024) · pending · bob", "2"));
        }

        [Test]
        public async Task request_card_buttons_follow_its_state_and_act_in_place()
        {
            await PendingMovie(Alice, Dune);
            var card = (await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin request").WithOption("request", "1")))).LastReply;
            ButtonIds(card).Should().Equal("req-approve:1", "req-deny:1", "req-available:1", "req-admin-cancel:1");

            var approved = await RequestCardButton("req-approve:1");

            approved.Responder.Replies.First().Embeds.Single().Description.Should().StartWith("✅ **Sent**, searching", "the card changes in place");
            ButtonIds(approved.Responder.Replies.First()).Should().Equal("req-admin-cancel:1");
            approved.LastReply.Ephemeral.Should().BeTrue();
            approved.LastReply.Content.Should().StartWith("Approved request #1");
            Reload(1).Status.Should().Be(RequestStatus.Approved);

            var late = await RequestCardButton("req-approve:1");
            late.LastReply.Content.Should().Be(DiscordText.NotPending(1, RequestStatus.Approved));
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

        [Test]
        public async Task the_queue_filters_by_type_and_opens_request_cards()
        {
            await PendingMovie(Alice, Dune);
            await AskSeasons(Bob, DarkMatter2015, 1);

            var queue = (await Harness.SendAsync(AsAdmin(FakeInteraction.Button(PanelRoutes.AdminQueue)))).LastReply;
            queue.Content.Should().StartWith(DiscordText.QueueHeader(null, 2));
            queue.Buttons.Select(b => b.CustomId).Should().Equal("adm-queue:all", "adm-queue:movie", "adm-queue:tv", "adm-queue:music");

            var tv = (await Harness.SendAsync(AsAdmin(FakeInteraction.Button("adm-queue:tv")))).LastReply;
            tv.Content.Should().StartWith(DiscordText.QueueHeader("TV", 1)).And.Contain("#2 ·");

            var card = (await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu(AdminRoutes.Request, "2")))).LastReply;
            card.Embeds.Single().Title.Should().Be("Dark Matter (2015)");
        }

        // Permissions: the card's custom IDs never carry authority.
        [Test]
        public async Task cards_and_their_buttons_are_admin_only_and_admin_channel_only()
        {
            (await Harness.SendAsync(FakeInteraction.SlashCommand("admin user").WithOption("member", Bob).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.Button($"adm-suspend:{Bob}").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.Button($"adm-suspend:{Bob}").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).LastReply.Content.Should().Be(DiscordText.AdminChannelOnly);
            (await Harness.SendAsync(FakeInteraction.Autocomplete("admin request", "request", "d").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).Responder.Suggestions.Should().BeEmpty();
            UserFor(Bob).Status.Should().Be(UserStatus.Active);

            (await Harness.SendAsync(FakeInteraction.Button($"adm-suspend:{Bob}").By(Moderator, "mod").WithRoles(AdminRole).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).LastReply.Content.Should().Be(DiscordText.UserSuspendedDone("bob"));
        }

        private async Task<InteractionReply> CardNow(ulong member) => (await MemberCard(member)).LastReply;
    }
}
