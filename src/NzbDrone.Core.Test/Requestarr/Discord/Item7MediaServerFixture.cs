using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Discord
{
    // Item 7: admin links a Plex username; users request access; approval links (no invite yet).
    [TestFixture]
    public class Item7MediaServerFixture : DiscordScenario
    {
        [SetUp]
        public async Task AddAlice()
        {
            await AddUser(Alice);
        }

        private MediaServerAccount Account(ulong discordId) => Mocker.Resolve<IMediaServerAccountRepository>().Find(UserFor(discordId).Id, MediaServerProvider.Plex);

        [Test]
        public async Task admin_should_link_plex_username()
        {
            var run = await Harness.SendAsync(AdminCommand("user mediaserver", Alice).WithOption("service", "plex").WithOption("username", "alice_plex"));

            run.LastReply.Content.Should().Be(DiscordText.MediaServerLinked("alice", MediaServerProvider.Plex, "alice_plex"));
            Account(Alice).Username.Should().Be("alice_plex");
            Account(Alice).ShareStatus.Should().Be(ShareStatus.None);
        }

        [Test]
        public async Task linking_again_should_update_the_username()
        {
            await Harness.SendAsync(AdminCommand("user mediaserver", Alice).WithOption("service", "plex").WithOption("username", "old"));
            await Harness.SendAsync(AdminCommand("user mediaserver", Alice).WithOption("service", "plex").WithOption("username", "new"));

            Mocker.Resolve<IMediaServerAccountRepository>().GetForUser(UserFor(Alice).Id).Should().ContainSingle().Which.Username.Should().Be("new");
        }

        [Test]
        public async Task linking_requires_a_requestarr_user_with_a_channel()
        {
            var run = await Harness.SendAsync(AdminCommand("user mediaserver", Bob).WithOption("service", "plex").WithOption("username", "bob_plex"));

            run.LastReply.Content.Should().Be(DiscordText.NotARequestarrUser);
        }

        [Test]
        public async Task user_request_should_post_approve_and_deny_card_in_admin_channel()
        {
            var run = await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "alice_plex"));

            run.LastReply.Content.Should().Be(DiscordText.AccessRequestSent(MediaServerProvider.Plex));
            run.LastReply.Ephemeral.Should().BeTrue();

            var card = Discord.Channels[AdminChannel].Messages.Single();
            card.Content.Should().Contain("alice_plex");
            card.Buttons.Select(b => b.CustomId).Should().Equal("msreq-approve:1", "msreq-deny:1");
            Account(Alice).Should().BeNull();
        }

        [Test]
        public async Task second_pending_request_should_be_refused()
        {
            await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "a"));

            (await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "b"))).LastReply.Content.Should().Be(DiscordText.AccessRequestAlreadyPending);
        }

        [Test]
        public async Task approve_should_link_account_update_card_and_tell_user()
        {
            await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "alice_plex"));

            var run = await Harness.SendAsync(FakeInteraction.Button("msreq-approve:1").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.Responder.Responses.Single().Kind.Should().Be(ResponseKind.MessageUpdate);
            run.LastReply.Content.Should().Contain("approved").And.Contain("alice_plex");
            run.LastReply.Buttons.Should().BeEmpty();
            Account(Alice).Username.Should().Be("alice_plex");
            ChannelFor(Alice).Messages.Last().Content.Should().Be(DiscordText.AccessRequestApprovedUser(MediaServerProvider.Plex, "alice_plex"));
        }

        [Test]
        public async Task deny_should_not_link_account()
        {
            await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "alice_plex"));

            var run = await Harness.SendAsync(FakeInteraction.Button("msreq-deny:1").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Contain("denied");
            Account(Alice).Should().BeNull();
            ChannelFor(Alice).Messages.Last().Content.Should().Be(DiscordText.AccessRequestDeniedUser(MediaServerProvider.Plex));
        }

        [Test]
        public async Task deciding_twice_or_unknown_request_should_be_refused()
        {
            await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "alice_plex"));
            await Harness.SendAsync(FakeInteraction.Button("msreq-approve:1").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            (await Harness.SendAsync(FakeInteraction.Button("msreq-deny:1").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel)))
                .LastReply.Content.Should().Be(DiscordText.AccessRequestAlreadyDecided);
            (await Harness.SendAsync(FakeInteraction.Button("msreq-approve:99").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel)))
                .LastReply.Content.Should().Be(DiscordText.AccessRequestNotFound);
            (await Harness.SendAsync(FakeInteraction.Button("msreq-approve:abc").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel)))
                .LastReply.Content.Should().Be(DiscordText.AccessRequestNotFound);
        }

        [Test]
        public async Task blank_username_should_be_refused()
        {
            (await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "  "))).LastReply.Content.Should().Be(DiscordText.InvalidUsername);
        }
    }
}
