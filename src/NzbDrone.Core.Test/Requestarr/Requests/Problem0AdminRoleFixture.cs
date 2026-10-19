using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Discord.Harness;
using GatewayPermissions = Requestarr.Core.Discord.Gateway.ChannelPermissions;

namespace Requestarr.Core.Test.Requests
{
    // Problem 0 and 0b: the admin role exists automatically; admin-only commands are hidden.
    [TestFixture]
    public class Problem0AdminRoleFixture : DiscordScenario
    {
        private async Task<DiscordGuildConfig> FreshSetup(DiscordSettings settings)
        {
            Mocker.Resolve<IDiscordSettingsStore>().Set(settings);
            Discord.Roles.Remove(AdminRole);
            Mocker.Resolve<IDiscordGuildConfigRepository>().Purge();
            return await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
        }

        private static DiscordSettings Settings(ulong? roleId = null, string roleName = null) =>
            new() { BotToken = "t", ApplicationId = 1, GuildId = FakeDiscordGuild.Guild, AdminRoleId = roleId, AdminRoleName = roleName ?? DiscordSettings.DefaultAdminRoleName };

        [Test]
        public async Task setup_should_create_requestarr_admin_role_when_missing()
        {
            var config = await FreshSetup(Settings());

            var role = ulong.Parse(config.AdminRoleId);
            Discord.Roles[role].Should().Be("Requestarr Admin");
            AuditTrail.Should().Contain(a => a.Action == "discord.admin-role.create");
        }

        [Test]
        public async Task setup_should_reuse_role_with_the_configured_name()
        {
            var existing = await Discord.CreateRoleAsync("Media Admins");

            var config = await FreshSetup(Settings(roleName: "Media Admins"));

            config.AdminRoleId.Should().Be(existing.ToString());
            Discord.Roles.Values.Count(r => r == "Media Admins").Should().Be(1);
        }

        [Test]
        public async Task setup_should_prefer_explicit_role_id()
        {
            var explicitRole = await Discord.CreateRoleAsync("Staff");

            var config = await FreshSetup(Settings(explicitRole));

            config.AdminRoleId.Should().Be(explicitRole.ToString());
            Discord.Roles.Values.Should().NotContain("Requestarr Admin");
        }

        [Test]
        public async Task setup_should_add_the_role_to_existing_admin_channel_and_categories()
        {
            var config = await FreshSetup(Settings());
            var role = ulong.Parse(config.AdminRoleId);

            OverwriteFor(Discord.Channels[AdminChannel], role).Allow.Should().HaveFlag(GatewayPermissions.ViewChannel);
            OverwriteFor(Discord.Channels[ulong.Parse(config.RequestCategoryIds.Single())], role).Allow.Should().HaveFlag(GatewayPermissions.ViewChannel);
        }

        [Test]
        public async Task holding_the_created_role_makes_a_member_admin()
        {
            var config = await FreshSetup(Settings());
            var role = ulong.Parse(config.AdminRoleId);

            var interaction = FakeInteraction.SlashCommand("admin user info").By(Bob).WithRoles(role).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel).Build();

            Mocker.Resolve<ICallerResolver>().Resolve(interaction).IsAdmin.Should().BeTrue();
        }

        [Test]
        public async Task new_user_channels_should_allow_the_created_admin_role()
        {
            var config = await FreshSetup(Settings());
            await AddUser(Alice);

            OverwriteFor(ChannelFor(Alice), ulong.Parse(config.AdminRoleId)).Allow.Should().HaveFlag(GatewayPermissions.ViewChannel);
        }

        [Test]
        public void admin_only_commands_should_require_manage_server_and_shared_ones_should_not()
        {
            var commands = DiscordCommandDefinitions.Build().Cast<SlashCommandProperties>().ToDictionary(c => c.Name.Value);

            commands.Keys.Should().BeEquivalentTo("admin", "request");
            commands["admin"].DefaultMemberPermissions.Value.Should().Be(GuildPermission.ManageGuild);
            commands["request"].DefaultMemberPermissions.IsSpecified.Should().BeFalse();
        }

        [Test]
        public async Task hidden_commands_are_still_refused_server_side_for_non_admins()
        {
            await AddUser(Alice);

            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("admin queue").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.AdminOnly);
        }
    }
}
