using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using Requestarr.Core.Removals;

namespace Requestarr.Core.Test.Removals
{
    // Task 4b: dev fast votes. On the dev stack, `dev.ps1 fast-votes on` makes removal votes close after
    // 5 minutes and requesters answer within 5 (reminded at minute 3); the admin channel says so. It can't
    // be on anywhere else, and off is the real day values.
    [TestFixture]
    public class Item4FastVotesFixture : RemovalScenario
    {
        private void FastVotes(bool on) => Mocker.GetMock<IDevFastVotes>().SetupGet(f => f.On).Returns(on);

        private System.Collections.Generic.List<string> AdminNotices =>
            Discord.Channels[AdminChannel].Messages.Select(m => m.Content).Where(c => c != null && c.StartsWith("🧪")).ToList();

        [Test]
        public async Task off_by_default_the_real_day_values_apply()
        {
            var settings = Mocker.Resolve<IRemovalSettings>();

            (settings.FastVotes, settings.VoteWindow, settings.RequesterTimeout, settings.ReminderAfter).Should().Be((false, TimeSpan.FromDays(7), TimeSpan.FromDays(7), TimeSpan.FromDays(5)));
            await Removals.TickAsync();
            AdminNotices.Should().BeEmpty("nothing to say while it's off");
        }

        [Test]
        public async Task with_fast_votes_a_users_removal_closes_on_its_own_after_five_minutes()
        {
            FastVotes(true);
            await Tick(TimeSpan.Zero);
            AdminNotices.Single().Should().StartWith("🧪 **Dev: fast votes are on.** Removal votes now close after 5 minutes, and requesters have 5 minutes to answer (reminded after 3 minutes).")
                .And.Contain("not the real settings (7 days, 7 days, reminder on day 5)");

            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);
            Proposal().WindowClosesAt.Should().Be(Now.AddMinutes(5));

            await Tick(TimeSpan.FromMinutes(4));
            Proposal().Status.Should().Be(RemovalStatus.Voting);
            await Tick(TimeSpan.FromMinutes(1));

            Proposal().Status.Should().Be(RemovalStatus.Deleted);
            Radarr.Deletes.Should().ContainSingle();
            AdminNotices.Should().ContainSingle("said once, not every minute");
        }

        [Test]
        public async Task with_fast_votes_an_unanswered_admin_removal_reminds_at_minute_three_and_votes_at_five()
        {
            FastVotes(true);
            var request = await DownloadedMovie();
            await AdminStart(request);
            Proposal().AnswerBy.Should().Be(Now.AddMinutes(5));
            Latest(Alice).Content.Should().Contain("you'll get a reminder");

            await Tick(TimeSpan.FromMinutes(2));
            MessagesOf(Alice, "Reminder").Should().BeEmpty();
            await Tick(TimeSpan.FromMinutes(1));
            MessagesOf(Alice, "Reminder").Should().ContainSingle();
            await Tick(TimeSpan.FromMinutes(2));

            Proposal().Status.Should().Be(RemovalStatus.Voting);
            Proposal().RequesterResponse.Should().Be(RequesterResponse.Silent);
            await Tick(TimeSpan.FromMinutes(5));
            Proposal().Status.Should().Be(RemovalStatus.Deleted);
        }

        [Test]
        public async Task turning_it_off_is_announced_and_back_to_days()
        {
            FastVotes(true);
            await Tick(TimeSpan.Zero);
            FastVotes(false);
            await Tick(TimeSpan.FromMinutes(1));

            AdminNotices.Last().Should().Be("🧪 **Dev: fast votes are off.** Removal timings are back to the real settings: votes 7 days, requesters 7 days, reminder on day 5.");
            Mocker.Resolve<IRemovalSettings>().VoteWindow.Should().Be(TimeSpan.FromDays(7));
        }

        [Test]
        public void both_dev_scripts_have_every_command_and_the_docs_explain_them()
        {
            var ps1 = File.ReadAllText(AnimeDub.Item4To6Fixture.RepoFile("docker", "dev.ps1"));
            var sh = File.ReadAllText(AnimeDub.Item4To6Fixture.RepoFile("docker", "dev.sh"));
            var compose = File.ReadAllText(AnimeDub.Item4To6Fixture.RepoFile("docker", "compose.yaml"));
            var docs = File.ReadAllText(AnimeDub.Item4To6Fixture.RepoFile("docs", "development.md"));

            foreach (var command in new[] { "init", "up", "check", "stacks", "down", "reset-test-backends", "reset-qa", "reset-uat", "reset-dev", "logs", "stop-backend", "start-backend", "fake-download", "fast-votes" })
            {
                ps1.Should().Contain($"'{command}'", $"dev.ps1 has {command}");
                System.Text.RegularExpressions.Regex.IsMatch(sh, $@"\n    ([a-z-]+\|)*{command}(\|[a-z-]+)*\)").Should().BeTrue($"dev.sh has {command}");
            }

            ps1.Where(c => c > 127).Should().BeEmpty("Windows PowerShell 5.1 misreads non-ASCII in a script saved without a byte order mark");
            compose.Should().Contain($"{DevFastVotes.DevStackVariable}: \"true\"", "only the local test stacks' container may honour fast votes");
            docs.Should().Contain("### Helpers for testing by hand").And.Contain("Set-ExecutionPolicy -Scope CurrentUser RemoteSigned").And.Contain("fake-download movie").And.Contain("fast-votes on").And.Contain("stop-backend sonarr");
        }

        [Test]
        public void it_is_only_ever_on_for_the_dev_stack_with_the_marker_file()
        {
            var folder = Path.Combine(Path.GetTempPath(), "requestarr-fastvotes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            try
            {
                var folders = new Mock<IAppFolderInfo>();
                folders.SetupGet(f => f.AppDataFolder).Returns(folder);
                var devStack = "true";
                var fast = new DevFastVotes(folders.Object) { Variable = name => name == DevFastVotes.DevStackVariable ? devStack : null };

                fast.On.Should().BeFalse("no marker file");
                File.WriteAllText(Path.Combine(folder, DevFastVotes.MarkerFile), string.Empty);
                fast.On.Should().BeTrue();

                devStack = null;
                fast.On.Should().BeFalse("outside the dev stack the marker file is ignored");
                devStack = "false";
                fast.On.Should().BeFalse();
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
