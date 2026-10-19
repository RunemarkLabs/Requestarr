using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Discord
{
    // Item 2: admin channel and request category exist without manual setup; who is admin.
    [TestFixture]
    public class Item2AdminSpaceFixture : DiscordScenario
    {
        [Test]
        public void first_connect_should_create_admin_channel_and_category()
        {
            var admin = Discord.ChannelNamed(ChannelLayout.AdminChannelName);
            var category = Discord.ChannelNamed(ChannelLayout.RequestCategoryName);

            admin.Should().NotBeNull();
            category.Should().NotBeNull();
            category.IsCategory.Should().BeTrue();
            Config.AdminChannelId.Should().Be(admin.Id.ToString());
            Config.RequestCategoryIds.Should().Equal(category.Id.ToString());
        }

        [Test]
        public void admin_channel_should_be_hidden_from_everyone_but_admin_role_and_bot()
        {
            var admin = Discord.ChannelNamed(ChannelLayout.AdminChannelName);

            OverwriteFor(admin, FakeDiscordGuild.Guild).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(admin, AdminRole).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(admin, FakeDiscordGuild.Bot).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            admin.Overwrites.Should().HaveCount(3);
        }

        [Test]
        public async Task setup_should_reuse_existing_channel_and_category()
        {
            var channelsBefore = Discord.Channels.Count;

            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            Discord.Channels.Count.Should().Be(channelsBefore);
        }

        [Test]
        public async Task setup_should_adopt_channels_found_by_name_after_database_reset()
        {
            var adminId = Discord.ChannelNamed(ChannelLayout.AdminChannelName).Id;
            Mocker.Resolve<IDiscordGuildConfigRepository>().Purge();

            var config = await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            config.AdminChannelId.Should().Be(adminId.ToString());
            Discord.Channels.Values.Count(c => c.Name == ChannelLayout.AdminChannelName).Should().Be(1);
        }

        [Test]
        public async Task setup_should_recreate_admin_channel_deleted_in_discord()
        {
            var oldId = AdminChannel;
            await Discord.DeleteChannelAsync(oldId);

            var config = await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            config.AdminChannelId.Should().NotBe(oldId.ToString());
            Discord.ChannelNamed(ChannelLayout.AdminChannelName).Should().NotBeNull();
        }

        [Test]
        public void server_owner_should_be_admin()
        {
            var caller = Mocker.Resolve<ICallerResolver>().Resolve(AdminCommand("user info", Alice).Build());

            caller.IsAdmin.Should().BeTrue();
            caller.AdminDenial.Should().BeNull();
        }

        [Test]
        public void admin_role_member_should_be_admin()
        {
            var interaction = FakeInteraction.SlashCommand("admin user info").By(Moderator).WithRoles(AdminRole).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel).Build();

            Mocker.Resolve<ICallerResolver>().Resolve(interaction).IsAdmin.Should().BeTrue();
        }

        [Test]
        public void ordinary_member_should_not_be_admin()
        {
            var interaction = FakeInteraction.SlashCommand("admin user info").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel).Build();

            Mocker.Resolve<ICallerResolver>().Resolve(interaction).IsAdmin.Should().BeFalse();
        }
    }
}
