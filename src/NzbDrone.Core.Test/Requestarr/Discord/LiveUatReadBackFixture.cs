using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common.Categories;
using Requestarr.Core.Discord;

namespace Requestarr.Core.Test.Discord
{
    // Read-only check of the running UAT stack in the Discord test server: registered commands and pinned panels.
    // REST only (no gateway session), and it never creates, edits or deletes anything.
    // Never runs in CI (ManualTest). Run with the DISCORD_* variables exported:
    //   dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveUatReadBackFixture" --logger "console;verbosity=detailed"
    [TestFixture]
    [ManualTest]
    public class LiveUatReadBackFixture
    {
        [Test]
        public async Task uat_server_has_two_commands_and_pinned_panels()
        {
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");

            if (string.IsNullOrWhiteSpace(token))
            {
                Assert.Ignore("DISCORD_BOT_TOKEN not set");
            }

            using var client = new DiscordRestClient();
            await client.LoginAsync(TokenType.Bot, token);

            var guild = await client.GetGuildAsync(ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_TEST_GUILD_ID")));
            var botRole = guild.Roles.Single(r => r.Tags?.BotId == client.CurrentUser.Id);
            Log($"[bot] {client.CurrentUser.Username} in '{guild.Name}'; role '{botRole.Name}' Pin Messages: {botRole.Permissions.PinMessages}");

            var commands = (await guild.GetApplicationCommandsAsync()).OrderBy(c => c.Name).ToList();
            var global = await client.GetGlobalApplicationCommands();

            foreach (var command in commands)
            {
                var perms = command.DefaultMemberPermissions.RawValue == 0 ? "none (visible to everyone)" : string.Join(", ", command.DefaultMemberPermissions.ToList());
                Log($"[commands] /{command.Name,-8} default member permission: {perms}");
            }

            Log($"[commands] registered on the server: {string.Join(", ", commands.Select(c => "/" + c.Name))}; global: {global.Count}");

            var channels = await guild.GetTextChannelsAsync();
            var categories = (await guild.GetCategoryChannelsAsync()).Where(c => c.Name.StartsWith(ChannelLayout.RequestCategoryName)).Select(c => c.Id).ToHashSet();
            var admin = channels.Single(c => c.Name == ChannelLayout.AdminChannelName);
            var userChannels = channels.Where(c => c.CategoryId.HasValue && categories.Contains(c.CategoryId.Value) && c.Name.StartsWith(ChannelLayout.UserChannelPrefix)).ToList();

            Log($"[channels] #{admin.Name}; request channels: {string.Join(", ", userChannels.Select(c => "#" + c.Name))}");
            userChannels.Should().OnlyHaveUniqueItems(c => c.Name, "each user has exactly one request channel");
            commands.Select(c => c.Name).Should().Equal("admin", "help", "request");
            commands.Single(c => c.Name == "admin").DefaultMemberPermissions.ManageGuild.Should().BeTrue();
            commands.Where(c => c.Name != "admin").Should().OnlyContain(c => c.DefaultMemberPermissions.RawValue == 0UL);
            global.Should().BeEmpty();

            await CheckPanel(admin, PanelRoutes.AdminQueue, client.CurrentUser.Id);

            foreach (var channel in userChannels)
            {
                await CheckPanel(channel, PanelRoutes.MyRequests, client.CurrentUser.Id);
            }
        }

        private static async Task CheckPanel(RestTextChannel channel, string firstButton, ulong botId)
        {
            var pinned = await channel.GetPinnedMessagesAsync();
            var panels = pinned.Where(m => m.Author.Id == botId && Buttons(m).Any(b => b.CustomId == firstButton)).ToList();
            var panel = panels.FirstOrDefault();
            var buttons = panel == null ? string.Empty : string.Join(" ", Buttons(panel).Select(b => $"[{b.Label}]"));
            var lines = panel?.Content.Split('\n') ?? Array.Empty<string>();

            Log($"[panel] #{channel.Name}: {panels.Count} pinned panel(s); \"{lines.FirstOrDefault()}\" {buttons}");

            if (lines.Length > 1)
            {
                Log($"[panel]   {string.Join(" | ", lines.Skip(1))}");
            }

            panels.Should().ContainSingle($"#{channel.Name} has exactly one pinned panel");
        }

        private static ButtonComponent[] Buttons(IMessage message) =>
            message.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToArray();

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
