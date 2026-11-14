using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Qa;
using Requestarr.Core.Test.AnimeDub;

namespace Requestarr.Core.Test.Qa
{
    // The QA test checklist: QA-CHECKLIST.md, which versions each server tests, and passing, failing and completing it.
    [TestFixture]
    public class QaChecklistFixture : CoreTest<QaChecklistService>
    {
        private const string Text = @"# QA checklist

## 0.4.15 (2026-10-25)
- **Passing an item:** Press Passes. You should see it passed.
- **Failing an item:** Press Failing. You should see it failing.

## 0.4.14 (2026-10-25)

## 0.4.13 (2026-10-25)
- **Sign-in logo:** Sign out. You should see the logo.
";

        private readonly Dictionary<string, QaChecklistItem> _records = new();
        private int _nextId = 1;

        [SetUp]
        public void Setup()
        {
            _records.Clear();
            var items = Mocker.GetMock<IQaChecklistItemRepository>();
            items.Setup(r => r.FindByKey(It.IsAny<string>())).Returns<string>(k => _records.TryGetValue(k, out var r) ? r : null);
            items.Setup(r => r.Insert(It.IsAny<QaChecklistItem>())).Returns<QaChecklistItem>(r =>
            {
                r.Id = _nextId++;
                _records[r.ItemKey] = r;
                return r;
            });
            items.Setup(r => r.Update(It.IsAny<QaChecklistItem>())).Returns<QaChecklistItem>(r => _records[r.ItemKey] = r);

            Subject.ChecklistText = () => Text;
            Subject.CurrentVersion = () => new Version(0, 4, 15, 123);
            OnQa();
        }

        [Test]
        public void the_shipped_checklist_is_embedded_and_starts_at_the_changelogs_newest_version()
        {
            var file = File.ReadAllText(Item4To6Fixture.RepoFile("QA-CHECKLIST.md"));
            var changelog = File.ReadAllText(Item4To6Fixture.RepoFile("CHANGELOG.md"));
            var running = new Version(9, 9, 9);
            var versions = QaChecklist.Parse(file, running);

            QaChecklist.Embedded().Replace("\r\n", "\n").Should().Be(file.Replace("\r\n", "\n"));
            QaChecklist.Parse(file)[0].Version.Should().Be(Changelog.Parse(changelog)[0].Version, "the staging promotion numbers both files together");
            versions.SelectMany(v => v.Items).Select(i => i.Key).Should().OnlyHaveUniqueItems();
            versions.SelectMany(v => v.Items).Should().OnlyContain(i => i.Text.Contains("You should see"));
        }

        [Test]
        public void items_get_stable_keys_and_a_version_with_nothing_to_test_has_none()
        {
            var versions = QaChecklist.Parse(Text);

            versions.Select(v => v.Version.ToString()).Should().Equal("0.4.15", "0.4.14", "0.4.13");
            versions[0].Items.Select(i => i.Key).Should().Equal("0.4.15/passing-an-item", "0.4.15/failing-an-item");
            versions[1].Items.Should().BeEmpty();
            versions[2].Items.Single().Name.Should().Be("Sign-in logo");
        }

        [Test]
        public void unreleased_items_are_the_build_qa_runs_keyed_by_its_build_count()
        {
            const string Pending = "# QA checklist\n\n## Unreleased\n- **Build number:** open System, then Status. You should see the build's number.\n\n## 0.6.0 (2026-11-13)\n";
            var running = new Version(0, 7, 5);

            var unreleased = QaChecklist.Parse(Pending, running);

            unreleased.Select(v => v.Version).Should().Equal(running, new Version(0, 6, 0));
            unreleased[0].Items.Single().Key.Should().Be("build-5/build-number");
            QaChecklist.Parse(Pending, new Version(0, 8, 5))[0].Items.Single().Key.Should().Be("build-5/build-number", "shipping 0.7.0004 moves QA's minor, and a pass recorded in QA is kept");
            QaChecklist.Parse(Pending, new Version(0, 7, 6))[0].Items.Single().Key.Should().NotBe("build-5/build-number", "the next build's items start untested");
            QaChecklist.Parse(Pending).Select(v => v.Version).Should().Equal(new[] { new Version(0, 6, 0) }, "without a running build Unreleased is left out");
        }

        [Test]
        public void qa_tests_what_staging_doesnt_have_dev_tries_out_the_running_version_and_other_servers_have_none()
        {
            var versions = QaChecklist.Parse(Text);
            var running = new Version(0, 4, 15, 7);

            QaChecklist.ForServer(versions, Environment("qa", "0.4.13"), running).Select(v => v.Version.ToString()).Should().Equal("0.4.15", "0.4.14");
            QaChecklist.ForServer(versions, Environment("dev"), running).Select(v => v.Version.ToString()).Should().Equal("0.4.15");
            QaChecklist.ForServer(versions, Environment("staging"), running).Should().BeEmpty();
            QaChecklist.ForServer(versions, Environment(null), running).Should().BeEmpty();
            QaChecklist.HasChecklist(Environment("qa")).Should().BeTrue();
            QaChecklist.HasChecklist(Environment(null)).Should().BeFalse();
        }

        [Test]
        public async Task the_checklist_completes_when_every_item_has_passed_and_records_who_and_when()
        {
            Subject.State().Should().BeEquivalentTo(new { Total = 3, Passed = 0, Failing = 0, Complete = false });

            await Subject.MarkPassedAsync("0.4.15/passing-an-item", 1, "Alex");
            await Subject.MarkPassedAsync("0.4.15/failing-an-item", 1, "Alex");
            Subject.State().Complete.Should().BeFalse();

            await Subject.MarkPassedAsync("0.4.13/sign-in-logo", 2, "Sam");
            var state = Subject.State();

            state.Should().BeEquivalentTo(new { Total = 3, Passed = 3, Failing = 0, Complete = true, CompletedBy = "Sam" });
            state.Versions.Should().Equal("0.4.15", "0.4.14", "0.4.13");
            _records["0.4.13/sign-in-logo"].ByDiscordUserId.Should().Be("2");
            _records["0.4.13/sign-in-logo"].At.Should().NotBeNull();
        }

        [Test]
        public async Task a_failing_item_links_its_bug_report_and_blocks_completion_until_it_passes()
        {
            Mocker.GetMock<IBugReportRepository>().Setup(r => r.Find(9)).Returns(new BugReport { Id = 9, IssueUrl = "https://github.com/x/y/issues/1" });

            foreach (var key in new[] { "0.4.15/passing-an-item", "0.4.13/sign-in-logo" })
            {
                await Subject.MarkPassedAsync(key, 1, "Alex");
            }

            await Subject.MarkFailingAsync("0.4.15/failing-an-item", 1, "Alex", new BugReport { Id = 9 });
            var state = Subject.State();

            state.Should().BeEquivalentTo(new { Passed = 2, Failing = 1, Complete = false });
            state.Items.Single(i => i.Key == "0.4.15/failing-an-item").Should().BeEquivalentTo(new { State = "Failing", BugReportId = 9, IssueUrl = "https://github.com/x/y/issues/1" });

            await Subject.MarkPassedAsync("0.4.15/failing-an-item", 2, "Sam");
            Subject.State().Should().BeEquivalentTo(new { Passed = 3, Failing = 0, Complete = true });
        }

        [Test]
        public async Task a_new_promotion_keeps_what_was_recorded_adds_new_items_and_drops_what_staging_has()
        {
            await Subject.MarkPassedAsync("0.4.13/sign-in-logo", 1, "Alex");
            await Subject.MarkPassedAsync("0.4.15/passing-an-item", 1, "Alex");

            // Staging got 0.4.13, and QA got 0.4.16 with one new item.
            OnQa("0.4.13");
            Subject.ChecklistText = () => Text.Replace("# QA checklist", "# QA checklist\n\n## 0.4.16 (2026-10-26)\n- **New thing:** Do it. You should see it.");
            Subject.CurrentVersion = () => new Version(0, 4, 16, 1);
            var state = Subject.State();

            state.Versions.Should().Equal("0.4.16", "0.4.15", "0.4.14");
            state.Items.Select(i => (i.Key, i.State)).Should().Equal(("0.4.16/new-thing", "Pending"), ("0.4.15/passing-an-item", "Passed"), ("0.4.15/failing-an-item", "Pending"));
        }

        [Test]
        public async Task an_item_no_longer_on_the_checklist_cant_be_marked()
        {
            await Subject.MarkPassedAsync("0.3.0/old", 1, "Alex");

            _records.Should().NotContainKey("0.3.0/old");
        }

        private void OnQa(string staged = "")
        {
            var environment = Environment("qa", staged);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.VersionPosts).Returns(environment.VersionPosts);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.Staged).Returns(environment.Staged);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.IconSet).Returns(environment.IconSet);
            Mocker.GetMock<IGuildSetupService>().Setup(g => g.GetConfig()).Returns((DiscordGuildConfig)null);
        }

        private static ReleaseEnvironment Environment(string name, string staged = "") => new()
        {
            Variable = v => v switch
            {
                ReleaseEnvironment.EnvironmentVariable => name,
                ReleaseEnvironment.StagedVersionsVariable => staged,
                _ => null
            }
        };
    }
}
