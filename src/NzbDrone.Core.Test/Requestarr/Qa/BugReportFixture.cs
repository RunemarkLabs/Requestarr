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
        }

        [Test]
        public async Task with_filing_on_it_becomes_an_issue_labelled_with_environment_version_and_part_and_the_card_shows_the_link()
        {
            Settings(file: true, token: "secret");
            Mocker.GetMock<IGitHubIssues>()
                .Setup(g => g.CreateAsync("RunemarkLabs/Requestarr", "secret", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FiledIssue(77, "https://github.com/RunemarkLabs/Requestarr/issues/77"));

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
        public void an_issue_holds_only_what_was_typed_and_what_requestarr_adds_and_pings_nobody()
        {
            var report = new BugReport { Id = 3, Component = "removals", WhatHappened = "Vote never closed @everyone", Expected = "It closes", Environment = "dev", Version = "0.4.15", ReporterName = "Alex", Created = _now };
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
