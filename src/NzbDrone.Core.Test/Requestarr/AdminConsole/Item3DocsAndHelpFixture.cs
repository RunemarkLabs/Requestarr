using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.Test.AnimeDub;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Requests;

namespace Requestarr.Core.Test.AdminConsole
{
    // Task 3b: admin help and the docs describe the three commands and where everything went.
    [TestFixture]
    public class Item3DocsAndHelpFixture : RequestScenario
    {
        private static readonly string[] OldCommands =
        {
            "/admin user add", "/admin user access", "/admin user quota", "/admin user info", "/admin user suspend", "/admin user remove",
            "/admin user mediaserver", "/admin queue", "/admin approve", "/admin mediaserver approve", "/admin help"
        };

        [Test]
        public void admin_help_lists_exactly_the_new_commands()
        {
            HelpCatalog.AdminTopics.SelectMany(t => t.Commands).Distinct().Should().BeEquivalentTo("admin user", "admin request");
            HelpCatalog.AdminTopics.SelectMany(t => t.Buttons).Should().Contain(PanelButtons.Members);
        }

        [Test]
        public async Task the_members_topic_explains_the_card()
        {
            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "onboarding").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            topic.LastReply.Content.Should().StartWith("**Members: adding and managing**")
                .And.Contain($"`/admin user member`: {DiscordText.Commands.AdminUser}")
                .And.Contain(DiscordText.PanelMembers).And.Contain(DiscordText.AddMemberLabel);
        }

        [Test]
        public void the_command_reference_maps_every_old_command()
        {
            var commands = File.ReadAllText(Item4To6Fixture.RepoFile("docs", "commands.md"));
            var table = commands.Substring(commands.IndexOf("### Where the old admin commands went"));

            foreach (var old in OldCommands)
            {
                table.Should().Contain($"`{old}", $"{old} must say where it went");
            }

            table.Should().Contain("`unsuspend`").And.Contain("`deny` / `available` / `cancel`").And.Contain("`/admin mediaserver approve` / `deny`");
            commands.Should().Contain("### `/admin user member:@someone`").And.Contain("### `/admin request request:{number or title}`");
        }

        [Test]
        public void the_setup_guide_admin_section_describes_the_new_layout()
        {
            var guide = File.ReadAllText(Item4To6Fixture.RepoFile("docs", "setup-guide.md"));
            var admin = guide.Substring(guide.IndexOf("## 5. Admin setup in Discord"), guide.IndexOf("## 6. ") - guide.IndexOf("## 5. Admin setup in Discord"));

            admin.Should().Contain("**Add or find a member**").And.Contain("### 5.6 The member card").And.Contain("`/admin request`");
            admin.Should().NotContain("/admin user add").And.NotContain("/admin queue").And.NotContain("/admin approve").And.NotContain("/admin mediaserver");
        }
    }
}
