using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Test.AnimeDub;

namespace Requestarr.Core.Test.Releases
{
    // Task 4d: the staging (beta) environment on the owner's server, published images, the staging promotion, and the
    // changelog rule. What runs is shown live in the task's reply; these keep the files honest.
    [TestFixture]
    public class Item4dStagingFixture
    {
        // Line endings as on Linux, whatever the checkout made them.
        private static string Read(params string[] path) => File.ReadAllText(Item4To6Fixture.RepoFile(path)).Replace("\r", string.Empty);

        [Test]
        public void the_staging_compose_project_is_separate_from_production_and_holds_nothing_personal()
        {
            var compose = Read("deploy", "staging", "compose.yaml");

            compose.Should().Contain("name: requestarr-beta").And.Contain("name: requestarr-beta\n\nvolumes:".Replace("\n\nvolumes:", string.Empty));
            compose.Should().Contain("image: ghcr.io/runemarklabs/requestarr:${REQUESTARR_TAG:-staging}");
            compose.Should().Contain("REQUESTARR_ENVIRONMENT: staging", "beta keeps every what's new post, like production");
            compose.Should().NotContain("env_file", "the container gets only the values it names");
            compose.Should().Contain("networks:\n  default:\n    name: requestarr-beta");

            // Every host path and port comes from .env; the only media folders are the two beta ones.
            Regex.Matches(compose, @"^\s+- (?<host>[^$\s][^:]*):/", RegexOptions.Multiline).Select(m => m.Groups["host"].Value)
                .Should().OnlyContain(h => h.StartsWith("./") || h.EndsWith("-config"), "named volumes and this folder's files only");
            Regex.Matches(compose, @"""\d+:\d+""").Should().BeEmpty("no fixed host port");
            compose.Should().Contain("${BETA_TV_FOLDER}:/tv-beta").And.Contain("${BETA_MOVIES_FOLDER}:/movies-beta");
            Regex.Matches(compose, @"\$\{BETA_(TV|MOVIES)_FOLDER[^}]*\}:/(?<target>[\w/-]+)").Select(m => m.Groups["target"].Value).Distinct()
                .Should().BeEquivalentTo(new[] { "tv-beta", "movies-beta", "check/tv-beta", "check/movies-beta" });
            compose.Should().Contain("condition: service_completed_successfully", "Sonarr and Radarr wait for the folder check");

            var provisioning = Read("deploy", "staging", "provisioning.yml");
            Regex.Matches(provisioning, @"rootFolder: (\S+)").Select(m => m.Groups[1].Value).Should().Equal("/tv-beta", "/movies-beta");
            provisioning.Should().Contain("webhookUrl: http://requestarr:9797");
            Regex.Matches(provisioning, @"^\s+(botToken|applicationId|guildId|apiKey|password|username): (?<value>.+)$", RegexOptions.Multiline)
                .Select(m => m.Groups["value"].Value).Should().OnlyContain(v => v.StartsWith("${"), "every secret comes from .env");
        }

        [Test]
        public void the_template_leaves_every_personal_value_for_the_owner()
        {
            var template = Read("deploy", "staging", "env.example");
            var values = Regex.Matches(template, @"^(?<name>[A-Z_]+)=(?<value>.*)$", RegexOptions.Multiline).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);

            var owners = "BETA_TV_FOLDER BETA_MOVIES_FOLDER BETA_DOWNLOADS_FOLDER BETA_REQUESTARR_PORT BETA_SONARR_PORT BETA_RADARR_PORT PUID PGID BETA_SONARR_API_KEY " +
                "BETA_RADARR_API_KEY REQUESTARR_API_KEY BETA_DISCORD_BOT_TOKEN BETA_DISCORD_APPLICATION_ID BETA_DISCORD_GUILD_ID TMDB_API_KEY";
            values.Keys.Should().Contain(owners.Split(' '));
            values.Where(v => v.Key != "BETA_DOWNLOADS_PATH" && v.Key != "TZ" && v.Key != "BETA_QUALITY_PROFILE").Should().OnlyContain(v => v.Value == string.Empty, "paths, ports, IDs and secrets are the owner's");

            Read(".gitignore").Should().Contain("deploy/staging/.env").And.Contain("deploy/staging/backups/");
            Read(".dockerignore").Should().Contain("**/*.env").And.Contain("**/.env").And.Contain("docker/secrets.env");
        }

        [Test]
        public void the_folder_check_refuses_anything_that_isnt_beta_only()
        {
            var check = Read("deploy", "staging", "check-folders.sh");

            check.Should().Contain("*-beta)").And.Contain(".requestarr-beta").And.Contain("is inside the downloads folder").And.Contain("are the same folder");
            Read("deploy", "staging", "root-folders.sh").Should().Contain("/tv-beta").And.Contain("/movies-beta");
        }

        [Test]
        public void updating_and_rolling_back_is_one_command()
        {
            var update = Read("deploy", "staging", "update.sh");

            update.Should().Contain("update) update ;;").And.Contain("rollback) rollback ;;").And.Contain("status) status ;;");
            update.Should().Contain("fetch \"$IMAGE:staging\"").And.Contain("fetch \"$IMAGE:$version\"").And.Contain("tar czf").And.Contain("pin \"$version\"");

            // Task 4d fix in staging: a failed download says why and falls back to the copy already on the server.
            update.Should().Contain("Couldn't download $ref").And.Contain("Using the copy of $ref already on this server").And.Contain("there's no copy of $ref on this server");
            Read("docs", "setup-guide.md").Should().Contain("docker login ghcr.io").And.Contain("read:packages");

            // A backup that fails stops the update and starts the old version again: a rollback needs that data.
            update.Should().Contain("if ! file=$(backup \"$before\"); then").And.Contain("[ -s \"$file\" ] || return 1").And.Contain("MSYS_NO_PATHCONV=1");
        }

        [Test]
        public void images_are_published_for_two_architectures_on_staging_and_main_only()
        {
            var image = Read(".github", "workflows", "image.yml");

            image.Should().Contain("branches: [staging, main]").And.Contain("platforms: linux/amd64,linux/arm64").And.Contain("packages: write");
            image.Should().Contain("version_rules.py beta \"$version\" \"$GITHUB_SHA\"").And.Contain("tags=\"$IMAGE:staging,$IMAGE:$release\"").And.Contain("push:main) tags=\"$IMAGE:$version,$IMAGE:${version%.*},$IMAGE:latest\"");
            image.Should().Contain("RELEASE_VERSION=${{ steps.tags.outputs.beta }}").And.Contain("git push origin \"refs/tags/v$BETA\"", "each staging publish is recorded as a tag the next beta number is counted from");
            Read("docker", "Dockerfile").Should().Contain($"ENV {ReleaseEnvironment.ReleaseVersionVariable}=$RELEASE_VERSION");
            image.Should().Contain("^test-[a-z0-9.-]+$", "a hand-run publishes only a test tag");
            image.Should().Contain("org.opencontainers.image.source=").And.Contain("secret-looking environment variable").And.Contain("answers /ping");
            Read("docker", "Dockerfile").Should().Contain("FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk").And.Contain("ARG TARGETARCH");
        }

        [Test]
        public void staging_has_its_promotion_fix_back_and_drift()
        {
            var release = Read("release.sh");
            var build = Read(".github", "workflows", "build.yml");

            release.Should().Contain("promote-staging) shift; promote qa staging").And.Contain("merge_into staging qa").And.Contain("merge_into qa dev");
            release.Should().Contain("drift_pair \"$qa\" \"$staging\"").And.Contain("version_rules.py\" check \"$target\" \"$new\"");
            release.Should().Contain("version_rules.py\" beta").And.Contain("version_rules.py\" merge").And.Contain("--dry-run) dry=1");
            build.Should().Contain("python3 .github/version_rules.py --self-test");
            build.Should().Contain("pull_request:staging) below=qa ;;").And.Contain("HEAD_REF: ${{ github.event.pull_request.head.ref }}");
            Read(".github", "workflows", "drift.yml").Should().Contain("staging_ref");
        }

        [Test]
        public void the_local_stacks_tell_requestarr_their_environment()
        {
            Read("docker", "compose.yaml").Should().Contain($"{ReleaseEnvironment.EnvironmentVariable}: ${{REQUESTARR_STACK}}").And.Contain(ReleaseEnvironment.StagedVersionsVariable);
            Read("docker", "dev.ps1").Should().Contain("staged-versions.py").And.Contain(ReleaseEnvironment.StagedVersionsVariable);
            Read("docker", "dev.sh").Should().Contain("staged-versions.py").And.Contain(ReleaseEnvironment.StagedVersionsVariable);
            Read("docker", "staged-versions.py").Should().Contain("actions/workflows/image.yml/runs").And.Contain("branch=staging").And.Contain("status=success")
                .And.Contain("origin/staging", "offline, the staging branch is the next best record");
        }

        [Test]
        public void the_changelog_has_two_audiences_and_is_checked()
        {
            var changelog = Read("CHANGELOG.md");

            changelog.Should().NotContain("### Internal").And.NotContain("nothing this time").And.NotContain("`");
            Regex.Matches(changelog, @"^### (.+)$", RegexOptions.Multiline).Select(m => m.Groups[1].Value.Trim()).Distinct().Should().BeEquivalentTo(new[] { "For users", "For admins" });
            Changelog.Parse(changelog).SelectMany(v => v.Users.Concat(v.Admins)).Should().OnlyContain(l => l.StartsWith("**"), "each line leads with what changed, in bold");

            var check = Read(".github", "changelog_check.py");
            check.Should().Contain("def self_test").And.Contain("nothing this time").And.Contain("minor fixes");
            Read(".github", "workflows", "build.yml").Should().Contain("python3 .github/changelog_check.py --self-test");
            Read(".github", "pull_request_template.md").Should().Contain("For users").And.Contain("For admins").And.NotContain("Internal");
            Read("CLAUDE.md").Should().Contain("only **For users** and **For admins**");
        }

        [Test]
        public void the_owner_has_a_server_guide_in_order()
        {
            var guide = Read("docs", "setup-guide.md");
            var start = guide.IndexOf("## Running the beta (staging) environment");

            start.Should().BeGreaterThan(0);
            var section = guide.Substring(start, guide.IndexOf("\n## ", start + 10) - start);
            var steps = Regex.Matches(section, @"^### B\.(\d+) ", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value)).ToList();
            steps.Should().Equal(Enumerable.Range(1, 10), "every step, in order");
            Regex.Matches(section, @"\*\*Check:\*\*").Count.Should().BeGreaterOrEqualTo(10, "each step says what to check");
            section.Should().Contain("Category `tv-beta`").And.Contain("Category `movies-beta`").And.Contain("./update.sh rollback").And.Contain("Automatic updates")
                .And.Contain("### Known limitation: beta can download a second copy").And.Contain("http://requestarr:9797");

            var development = Read("docs", "development.md");
            development.Should().Contain("### Published images").And.Contain("### Version posts").And.Contain("./release.sh promote-staging").And.Contain("linux/arm64");
        }
    }
}
