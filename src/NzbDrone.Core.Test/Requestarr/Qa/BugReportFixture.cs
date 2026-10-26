using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Qa;

namespace Requestarr.Core.Test.Qa
{
    // Bug reports from the Report a problem button: kept, posted, filed on GitHub only when the install files them, limited
    // per person, and holding nothing beyond what the reporter typed plus environment, version, time, part and name.
    [TestFixture]
    public class BugReportFixture : CoreTest<BugReportService>
    {
        private readonly List<BugReport> _reports = new();
        private BugReportGuard _guard;
        private DateTimeOffset _now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

        [SetUp]
        public void Setup()
        {
            _reports.Clear();
            _guard = null;
            Subject.Now = () => _now;

            Mocker.GetMock<IBugReportRepository>().Setup(r => r.Insert(It.IsAny<BugReport>())).Returns<BugReport>(r =>
            {
                r.Id = _reports.Count + 1;
                _reports.Add(r);
                return r;
            });
            Mocker.GetMock<IBugReportRepository>().Setup(r => r.Find(It.IsAny<int>())).Returns<int>(id => _reports.SingleOrDefault(r => r.Id == id));
            Mocker.GetMock<IBugReportRepository>().Setup(r => r.All()).Returns(() => _reports);
            Mocker.GetMock<IBugReportGuardRepository>().Setup(r => r.Find("42")).Returns(() => _guard);
            Mocker.GetMock<IBugReportGuardRepository>().Setup(r => r.Upsert(It.IsAny<BugReportGuard>())).Returns<BugReportGuard>(g => _guard = g);
            Mocker.GetMock<IGuildSetupService>().Setup(g => g.GetConfig()).Returns(new DiscordGuildConfig { BugReportsChannelId = "500" });
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.SendMessageAsync(500, It.IsAny<InteractionReply>())).ReturnsAsync(900UL);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.IconSet).Returns("qa");
            Settings(file: false, token: null);
        }

        [Test]
        public async Task with_filing_off_a_report_is_kept_and_posted_but_not_sent_anywhere()
        {
            var result = await Subject.SubmitAsync(42, "Alex", "requests", "The button did nothing", "A request", null);

            result.Report.Should().BeEquivalentTo(new { Component = "requests", WhatHappened = "The button did nothing", Expected = "A request", Environment = "qa", ReporterName = "Alex", Filing = BugReportFiling.Off, MessageId = "900" });
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.SendMessageAsync(500, It.Is<InteractionReply>(m => m.Cards[0].Text.Contains("Bug report #1"))), Times.Once());
            Mocker.GetMock<IGitHubIssues>().VerifyNoOtherCalls();
            result.Message.Should().Contain("<#500>");
            QaText.ReportCard(result.Report).Should().Contain("filing reports is turned off in this server's settings");
        }

        [Test]
        public async Task with_filing_on_it_becomes_an_issue_labelled_with_environment_version_and_part_and_the_card_shows_the_link()
        {
            Settings(file: true, token: "secret");
            Mocker.GetMock<IGitHubIssues>()
                .Setup(g => g.CreateAsync("RunemarkLabs/Requestarr", "secret", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IssueResult(new FiledIssue(77, "https://github.com/RunemarkLabs/Requestarr/issues/77"), null));

            var result = await Subject.SubmitAsync(42, "Alex", "web-app", "The logo is missing", null, null);

            result.Report.Should().BeEquivalentTo(new { Filing = BugReportFiling.Filed, IssueNumber = 77, IssueUrl = "https://github.com/RunemarkLabs/Requestarr/issues/77" });
            var labels = new[] { "bug report", "env: qa", $"version: {Changelog.Release(NzbDrone.Common.EnvironmentInfo.BuildInfo.Version)}", "component: web-app" };
            Mocker.GetMock<IGitHubIssues>().Verify(g => g.CreateAsync("RunemarkLabs/Requestarr", "secret", "[QA] Web app: The logo is missing", It.IsAny<string>(), It.Is<IReadOnlyList<string>>(l => l.SequenceEqual(labels)), It.IsAny<CancellationToken>()));
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.EditMessageAsync(500, 900, It.Is<InteractionReply>(m => m.Cards[0].Text.Contains("issue #77"))), Times.Once());
        }

        [Test]
        public async Task on_but_without_an_access_key_nothing_is_filed_and_the_card_says_why()
        {
            Settings(file: true, token: null);

            var result = await Subject.SubmitAsync(42, "Alex", "other", "Something", null, null);

            result.Report.Filing.Should().Be(BugReportFiling.NoToken);
            Mocker.GetMock<IGitHubIssues>().VerifyNoOtherCalls();
            QaText.ReportCard(result.Report).Should().Contain("no GitHub access key");
        }

        [Test]
        public async Task a_failing_checklist_item_gets_the_report_linked()
        {
            var item = new QaChecklistEntry(new Version(0, 4, 15), "0.4.15/report-limit", "Report limit", "Do it. You should see it.");
            Mocker.GetMock<IQaChecklistService>().Setup(c => c.Find(item.Key)).Returns(item);

            var result = await Subject.SubmitAsync(42, "Alex", "admin", "It let me through", null, item.Key);

            result.Report.ChecklistItemKey.Should().Be(item.Key);
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.MarkFailingAsync(item.Key, 42, "Alex", result.Report), Times.Once());
            QaText.IssueBody(result.Report, item).Should().Contain("**From the QA checklist:** Report limit (0.4.15)");
        }

        [Test]
        public async Task each_person_waits_between_reports_and_has_a_daily_ceiling()
        {
            (await Subject.SubmitAsync(42, "Alex", "other", "One", null, null)).Report.Should().NotBeNull();

            var second = await Subject.SubmitAsync(42, "Alex", "other", "Two", null, null);
            second.Report.Should().BeNull();
            second.Message.Should().Contain("You can send another");
            Subject.WaitLeft(42).Should().Be(TimeSpan.FromMinutes(2));

            for (var i = 0; i < 4; i++)
            {
                _now = _now.AddMinutes(3);
                (await Subject.SubmitAsync(42, "Alex", "other", "More", null, null)).Report.Should().NotBeNull();
            }

            // Five today: the next waits until the first is a day old.
            _now = _now.AddMinutes(3);
            Subject.WaitLeft(42).Should().Be(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(15));
            _reports.Should().HaveCount(5);
        }

        [Test]
        public async Task an_admin_failing_checklist_items_isnt_held_to_the_limit_and_uses_none_of_it()
        {
            for (var i = 0; i < 3; i++)
            {
                (await Subject.SubmitAsync(42, "Alex", "admin", "Failing " + i, null, null, limited: false)).Report.Should().NotBeNull();
            }

            _reports.Should().HaveCount(3);
            Subject.WaitLeft(42).Should().BeNull("a member's limit is untouched by checklist work");
        }

        [Test]
        public async Task a_refused_report_says_exactly_why_and_is_filed_on_the_next_try_without_being_sent_again()
        {
            Settings(file: true, token: "secret");
            var github = Mocker.GetMock<IGitHubIssues>();
            github.Setup(g => g.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IssueResult(null, "GitHub refused the access key (HTTP 401): it's wrong, revoked or expired"));

            var report = (await Subject.SubmitAsync(42, "Alex", "requests", "Stuck", null, null)).Report;

            report.Filing.Should().Be(BugReportFiling.Failed);
            QaText.ReportCard(report).Should().Contain("HTTP 401").And.Contain("tries again");

            github.Setup(g => g.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IssueResult(new FiledIssue(5, "https://github.com/RunemarkLabs/Requestarr/issues/5"), null));
            await Subject.RetryUnfiledAsync();
            await Subject.RetryUnfiledAsync();

            _reports.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Filing = BugReportFiling.Filed, IssueNumber = 5, FilingProblem = (string)null });
            github.Verify(g => g.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.EditMessageAsync(500, 900, It.Is<InteractionReply>(m => m.Cards[0].Text.Contains("issue #5"))), Times.Once());
        }

        [Test]
        public async Task a_report_kept_for_want_of_a_key_is_filed_once_the_key_is_there_and_its_checklist_item_follows()
        {
            var item = new QaChecklistEntry(new Version(0, 4, 16), "0.4.16/one", "One", "Do it. You should see it.");
            Mocker.GetMock<IQaChecklistService>().Setup(c => c.Find(item.Key)).Returns(item);
            Settings(file: true, token: null);

            var report = (await Subject.SubmitAsync(42, "Alex", "admin", "Broken", null, item.Key, limited: false)).Report;
            report.Filing.Should().Be(BugReportFiling.NoToken);

            Settings(file: true, token: "secret");
            Mocker.GetMock<IGitHubIssues>()
                .Setup(g => g.CreateAsync(It.IsAny<string>(), "secret", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IssueResult(new FiledIssue(6, "https://github.com/RunemarkLabs/Requestarr/issues/6"), null));
            await Subject.RetryUnfiledAsync();

            report.Filing.Should().Be(BugReportFiling.Filed);
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.RefreshItemAsync(item.Key), Times.Once());
        }

        [Test]
        public void the_test_servers_always_file_and_the_key_can_come_from_the_environment()
        {
            var environment = new Mock<IReleaseEnvironment>();
            var config = new Mock<NzbDrone.Core.Configuration.IConfigService>();
            var settings = new BugReportSettings(config.Object, environment.Object) { Variable = v => v == BugReportSettings.TokenVariable ? " from-env " : null };

            foreach (var set in new[] { "dev", "qa", "beta" })
            {
                environment.SetupGet(e => e.IconSet).Returns(set);
                settings.FileIssues.Should().BeTrue(set + " files every report");
            }

            environment.SetupGet(e => e.IconSet).Returns("production");
            settings.FileIssues.Should().BeFalse("someone else's install files only when its admin turns it on");
            settings.Token.Should().Be("from-env");

            settings.Apply(new Requestarr.Core.Provisioning.BugReportsProvisioning { GithubToken = "from-provisioning" });
            settings.Token.Should().Be("from-provisioning");
        }

        [Test]
        public void an_issue_holds_only_what_was_typed_and_what_requestarr_adds_and_pings_nobody()
        {
            var report = new BugReport { Id = 3, Component = "removals", WhatHappened = "Vote never closed @everyone", Expected = "It closes", Environment = "dev", Version = "0.4.15", ReporterName = "Alex", Created = _now.UtcDateTime };
            var body = QaText.IssueBody(report, null);

            body.Should().Contain("Vote never closed @​everyone").And.Contain("It closes").And.Contain("| Part | Removal votes |").And.Contain("| Environment | Dev |")
                .And.Contain("| Version | 0.4.15 |").And.Contain("| Reporter (Discord) | Alex |").And.NotContain("@everyone");
            QaText.IssueLabels(report).Should().Contain("test", "the dev server's issues are marked as tests");
            QaText.ReportCard(report).Should().NotContain("@everyone");
        }

        [Test]
        public void the_parts_are_one_list_and_anything_else_is_other()
        {
            BugReportComponents.All.Select(c => c.Key).Should().Equal("requests", "approvals", "availability", "removals", "access", "admin", "web-app", "other");
            BugReportComponents.NameOf("nonsense").Should().Be("Other");
        }

        private void Settings(bool file, string token)
        {
            var settings = Mocker.GetMock<IBugReportSettings>();
            settings.SetupGet(s => s.FileIssues).Returns(file);
            settings.SetupGet(s => s.Token).Returns(token);
            settings.SetupGet(s => s.Repository).Returns(BugReportSettings.DefaultRepository);
            settings.SetupGet(s => s.ReportsPerDay).Returns(BugReportSettings.DefaultReportsPerDay);
            settings.SetupGet(s => s.MinutesBetween).Returns(BugReportSettings.DefaultMinutesBetween);
        }
    }
}
