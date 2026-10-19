using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Discord
{
    // Item 4: category spill at 50 channels, role limit, Discord size limits.
    [TestFixture]
    public class Item4LimitsFixture : DiscordScenario
    {
        [Test]
        public async Task request_channels_should_spill_into_a_new_category_at_fifty()
        {
            var first = ulong.Parse(Config.RequestCategoryIds.Single());

            for (var i = 0; i < 49; i++)
            {
                await Discord.CreateTextChannelAsync($"filler-{i}", first, null, System.Array.Empty<PermissionOverwrite>());
            }

            await AddUser(Alice);
            ChannelFor(Alice).CategoryId.Should().Be(first);
            Discord.ChannelsIn(first).Count().Should().Be(50);

            await AddUser(Bob);
            var second = ChannelFor(Bob).CategoryId.Value;

            second.Should().NotBe(first);
            Discord.Channels[second].Name.Should().Be("Requestarr Requests 2");
            Config.RequestCategoryIds.Should().Equal(first.ToString(), second.ToString());
        }

        [Test]
        public async Task add_should_refuse_near_the_role_limit()
        {
            Discord.AddExistingRoles(DiscordLimits.RolesPerServer - DiscordLimits.RoleHeadroom - 2);

            var run = await AddUser(Alice);

            run.LastReply.Content.Should().Be(DiscordText.RoleLimit);
            UserFor(Alice).Should().BeNull();
        }

        [Test]
        public async Task add_should_still_work_just_below_the_role_limit()
        {
            // The scenario already has the admin role.
            Discord.AddExistingRoles(DiscordLimits.RolesPerServer - DiscordLimits.RoleHeadroom - 3);

            (await AddUser(Alice)).LastReply.Content.Should().StartWith("Added alice.");
        }

        [Test]
        public void slash_commands_should_build_within_discord_limits()
        {
            // Discord.Net validates names, descriptions (100), choice counts (25) on Build().
            var commands = DiscordCommandDefinitions.Build().Cast<SlashCommandProperties>().ToList();

            commands.Select(c => c.Name.Value).Should().BeEquivalentTo("user", "queue", "approve", "deny", "request", "requests", "quota", "mediaserver");

            foreach (var option in commands.SelectMany(c => c.Options.Value).SelectMany(o => o.Options ?? new()))
            {
                (option.Choices?.Count ?? 0).Should().BeLessThanOrEqualTo(DiscordLimits.ChoicesPerMenu);
                option.Description.Length.Should().BeLessThanOrEqualTo(DiscordLimits.OptionLabelLength);
                (option.Choices ?? new()).All(c => c.Name.Length <= DiscordLimits.OptionLabelLength).Should().BeTrue();
            }
        }

        [Test]
        public void names_should_be_sanitised_and_truncated()
        {
            ChannelLayout.UserChannelName("Ünïcødé Ālice!!").Should().Be("requests-ncd-lice");
            ChannelLayout.UserChannelName(new string('x', 300)).Length.Should().BeLessThanOrEqualTo(DiscordLimits.ChannelNameLength);
            ChannelLayout.UserRoleName(new string('y', 300)).Length.Should().BeLessThanOrEqualTo(DiscordLimits.RoleNameLength);
            ChannelLayout.UserChannelName("___").Should().Be("requests-user");
        }

        [Test]
        public void long_messages_should_be_truncated_to_discord_limit()
        {
            DiscordLimits.Truncate(new string('a', 5000), DiscordLimits.MessageLength).Length.Should().Be(DiscordLimits.MessageLength);
        }
    }
}
