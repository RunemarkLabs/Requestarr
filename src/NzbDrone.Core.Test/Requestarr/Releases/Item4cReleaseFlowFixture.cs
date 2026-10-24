using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Test.AnimeDub;

namespace Requestarr.Core.Test.Releases
{
    // Task 4c: the release flow. Pull requests merge to dev; qa moves only on the owner's word; fixes made in QA
    // are merged back so dev and qa never drift. Two local stacks side by side: QA (the owner's manual QA, the
    // default) and dev (Claude Code's automated testing on its own Discord server and bot).
    [TestFixture]
    public class Item4cReleaseFlowFixture
    {
        private static string Read(params string[] path) => File.ReadAllText(Item4To6Fixture.RepoFile(path));

        [Test]
        public void the_two_stacks_never_share_a_project_port_or_bot()
        {
            var compose = Read("docker", "compose.yaml");

            compose.Should().Contain("name: requestarr-${REQUESTARR_STACK:?", "each stack is its own Compose project: containers, volumes, network");
            compose.Should().Contain("image: requestarr:${REQUESTARR_STACK}");
            compose.Should().NotContain("env_file", "a stack gets only its own values, never the other stack's bot");
            compose.Should().Contain("DISCORD_BOT_TOKEN: ${STACK_DISCORD_BOT_TOKEN").And.Contain("DISCORD_TEST_GUILD_ID: ${STACK_DISCORD_GUILD_ID");
            compose.Should().Contain("${STACK_REQUESTARR_PORT:?").And.Contain("${STACK_SONARR_TEST_PORT:?").And.Contain("${STACK_RADARR_TEST_PORT:?");
            Regex.Matches(compose, @"127\.0\.0\.1:\d").Should().BeEmpty("no fixed host port: both stacks would clash");

            Read("docker", "dev.ps1").Should().Contain("Project = 'requestarr-qa'").And.Contain("Project = 'requestarr-dev'");
            Read("docker", "dev.sh").Should().Contain("PROJECT=\"requestarr-$STACK\"").And.Contain("        qa)").And.Contain("        dev)");

            foreach (var script in new[] { Read("docker", "dev.ps1"), Read("docker", "dev.sh") })
            {
                script.Should().Contain("DEV_DISCORD_BOT_TOKEN").And.Contain("DEV_DISCORD_GUILD_ID");
                script.Should().Contain("19797").And.Contain("28989").And.Contain("27878", "the dev stack's own ports");
                script.Should().Contain("reset-uat", "the old name still works");
                script.Should().Contain("archive --format=tar", "a stack runs exactly its branch's commit, never uncommitted files");
            }
        }

        [Test]
        public void qa_is_what_the_scripts_act_on_unless_told_otherwise()
        {
            Read("docker", "dev.ps1").Should().Contain("$env:REQUESTARR_STACK } else { 'qa' }");
            Read("docker", "dev.sh").Should().Contain("STACK=\"${REQUESTARR_STACK:-qa}\"");
        }

        [Test]
        public void live_fixtures_only_ever_use_the_dev_server_and_the_dev_stack()
        {
            var tests = Directory.GetFiles(Item4To6Fixture.RepoFile("src", "NzbDrone.Core.Test"), "*.cs", SearchOption.AllDirectories);
            var qa = new Regex(@"GetEnvironmentVariable\(""(DISCORD_BOT_TOKEN|DISCORD_APPLICATION_ID|DISCORD_TEST_GUILD_ID|DISCORD_ADMIN_ROLE_ID|RADARR_TEST_PORT|SONARR_TEST_PORT)""\)");

            tests.Where(f => qa.IsMatch(File.ReadAllText(f))).Select(Path.GetFileName).Should().BeEmpty("the QA server and stack are the owner's; live checks use LiveDev");

            tests.Where(f => File.ReadAllText(f).Contains("com.docker.compose.service=") && !File.ReadAllText(f).Contains("com.docker.compose.project={LiveDev.ComposeProject}"))
                .Select(Path.GetFileName).Should().BeEmpty("a container found by its service alone could be the QA stack's");
            tests.Where(f => Path.GetFileName(f) != "Item4cReleaseFlowFixture.cs" && File.ReadAllText(f).Contains("\"requestarr-qa-"))
                .Select(Path.GetFileName).Should().BeEmpty("live checks never name a QA container");

            var live = tests.Where(f => Path.GetFileName(f).StartsWith("Live") && File.ReadAllText(f).Contains("[ManualTest]")).ToList();
            live.Should().HaveCountGreaterThan(5);
            live.Where(f => File.ReadAllText(f).Contains("DiscordSettingsStore") && !File.ReadAllText(f).Contains("LiveDev.Settings()"))
                .Select(Path.GetFileName).Should().BeEmpty("every live bot is the dev bot in the dev server");
        }

        [Test]
        public void drift_promotion_and_versions_are_checked()
        {
            var release = Read("release.sh");
            var build = Read(".github", "workflows", "build.yml");
            var drift = Read(".github", "workflows", "drift.yml");

            release.Should().Contain("drift()").And.Contain("promote_qa()").And.Contain("merge_back()").And.Contain("check_changelog()");
            release.Should().Contain("git log --format='  %h %s' \"$dev..$qa\"", "drift is any commit on qa that dev doesn't have");
            release.Should().Contain("git push origin \"$target:refs/heads/qa\"", "a promotion fast-forwards qa: exactly dev's commits, nothing newer");
            release.Should().Contain("Requested by: $who");
            build.Should().Contain("./release.sh check-changelog").And.Contain("name: Promotion record").And.Contain("changelog, promotion]");
            build.Should().Contain("BODY: ${{ github.event.pull_request.body }}", "the body reaches the shell as data, never as script");
            drift.Should().Contain("branches: [dev, qa]").And.Contain("./release.sh drift");
        }

        [Test]
        public void the_flow_is_documented()
        {
            var docs = Read("docs", "development.md");

            docs.Should().Contain("## Release flow").And.Contain("promote to QA").And.Contain("./release.sh promote-qa").And.Contain("./release.sh merge-back");
            docs.Should().Contain("-Stack dev").And.Contain("reset-qa").And.Contain("reset-dev").And.Contain("DEV_DISCORD_BOT_TOKEN");
            docs.Should().NotContain("reset-uat`:", "QA, not UAT: reset-uat is only mentioned as the old name");
            Read("CLAUDE.md").Should().Contain("`dev`").And.Contain("`qa`").And.Contain("release.sh");
            Read(".github", "pull_request_template.md").Should().Contain("`dev`");
        }
    }
}
