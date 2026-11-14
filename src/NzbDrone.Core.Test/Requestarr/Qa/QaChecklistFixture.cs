using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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

        // The fold's 11 carried items, the two rewritten ones and the build-number item kept under Unreleased.
        private const int ItemsFromTheFold = 14;

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
            items.Setup(r => r.SetFields(It.IsAny<QaChecklistItem>(), It.IsAny<Expression<Func<QaChecklistItem, object>>[]>())).Callback<QaChecklistItem, Expression<Func<QaChecklistItem, object>>[]>((r, _) =>
            {
                // A changed key moves the record, as the database would find it.
                foreach (var stale in _records.Where(e => e.Value == r && e.Key != r.ItemKey).Select(e => e.Key).ToList())
                {
                    _records.Remove(stale);
                }

                _records[r.ItemKey] = r;
            });

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
            QaChecklist.Parse(file).Select(v => v.Version).Should().Equal(Changelog.Parse(changelog).Select(v => v.Version).Where(v => v >= new Version(0, 4, 3)), "the staging promotion numbers both files together");
            versions.SelectMany(v => v.Items).Select(i => i.Key).Should().OnlyHaveUniqueItems();
            versions.SelectMany(v => v.Items).Should().OnlyContain(i => i.Text.Contains("You should see"));
        }

        [Test]
        public void items_are_keyed_by_their_wording_and_a_version_with_nothing_to_test_has_none()
        {
            var versions = QaChecklist.Parse(Text);

            versions.Select(v => v.Version.ToString()).Should().Equal("0.4.15", "0.4.14", "0.4.13");
            versions[0].Items.Select(i => i.Key).Should().OnlyContain(k => k.Length < 60).And.SatisfyRespectively(
                k => k.Should().MatchRegex(@"^passing-an-item\.[0-9a-f]{8}$"),
                k => k.Should().MatchRegex(@"^failing-an-item\.[0-9a-f]{8}$"));
            versions[0].Items.Select(i => i.EarlierKey).Should().Equal("0.4.15/passing-an-item", "0.4.15/failing-an-item");
            versions[1].Items.Should().BeEmpty();
            versions[2].Items.Single().Name.Should().Be("Sign-in logo");
        }

        [Test]
        public void the_same_words_under_another_heading_are_the_same_item_and_new_words_are_a_new_one()
        {
            var moved = QaChecklist.Parse(Text.Replace("## 0.4.15 (2026-10-25)", "## Unreleased"), new Version(0, 7, 4));
            var reworded = QaChecklist.Parse(Text.Replace("You should see it passed.", "You should see a green tick."));

            moved[0].Items.Select(i => i.Key).Should().Equal(Keys("Passing an item", "Failing an item"), "a fold into Unreleased or a build number keeps the result");
            moved[0].Items[0].EarlierKey.Should().Be("build-4/passing-an-item");
            reworded[0].Items[0].Key.Should().NotBe(Key("Passing an item"), "changed words are a changed test");
            reworded[0].Items[1].Key.Should().Be(Key("Failing an item"));
            QaChecklist.Parse(Text.Replace("Press Passes.", "Press   Passes.")).SelectMany(v => v.Items).Select(i => i.Key).Should().Equal(Keys("Passing an item", "Failing an item", "Sign-in logo"), "spacing isn't wording");
        }

        [Test]
        public void unreleased_items_are_the_build_qa_runs()
        {
            const string Pending = "# QA checklist\n\n## Unreleased\n- **Build number:** open System, then Status. You should see the build's number.\n\n## 0.6.0 (2026-11-13)\n";
            var running = new Version(0, 7, 5);

            var unreleased = QaChecklist.Parse(Pending, running);

            unreleased.Select(v => v.Version).Should().Equal(running, new Version(0, 6, 0));
            QaChecklist.Parse(Pending, new Version(0, 8, 5))[0].Items.Single().Key.Should().Be(unreleased[0].Items.Single().Key, "shipping 0.7.0004 moves QA's minor, and a pass recorded in QA is kept");
            QaChecklist.Parse(Pending).Select(v => v.Version).Should().Equal(new[] { new Version(0, 6, 0) }, "without a running build Unreleased is left out");
        }

        [Test]
        public async Task a_promotion_keeps_unchanged_passes_and_a_reworded_or_new_item_comes_back_untested()
        {
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns("0.7.0004-qa.1111111");
            await Subject.MarkPassedAsync(Key("Passing an item"), 1, "Alex");
            await Subject.MarkPassedAsync(Key("Failing an item"), 1, "Alex");
            await Subject.MarkPassedAsync(Key("Sign-in logo"), 1, "Alex");

            // The next promotion: one item reworded, one new, all of it folded under Unreleased.
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns("0.7.0004-qa.2222222");
            Subject.ChecklistText = () => Text.Replace("## 0.4.15 (2026-10-25)", "## Unreleased\n- **New thing:** Do it. You should see it.")
                .Replace("You should see the logo.", "You should see the new logo.");
            Subject.CurrentVersion = () => new Version(0, 7, 4, 1);
            var state = Subject.State();

            state.Items.Select(i => (i.Name, i.State, i.By)).Should().Equal(
                ("New thing", "Pending", null), ("Passing an item", "Passed", "Alex"), ("Failing an item", "Passed", "Alex"), ("Sign-in logo", "Pending", null));
            state.Should().BeEquivalentTo(new { Total = 4, Passed = 2, Carried = 2, New = 2, Complete = false });
        }

        [Test]
        public void a_result_recorded_by_version_and_name_moves_to_its_wording_with_its_bug_report()
        {
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns("0.7.0004-qa.2222222");
            var report = new BugReport { Id = 9, ChecklistItemKey = "0.4.15/failing-an-item" };
            var earlier = new BugReport { Id = 8, ChecklistItemKey = "0.4.15/failing-an-item" };
            Mocker.GetMock<IBugReportRepository>().Setup(r => r.Find(9)).Returns(report);
            Mocker.GetMock<IBugReportRepository>().Setup(r => r.All()).Returns(new[] { earlier, report, new BugReport { Id = 7, ChecklistItemKey = "0.4.15/other" } });
            _records["0.4.15/failing-an-item"] = new QaChecklistItem { Id = 50, ItemKey = "0.4.15/failing-an-item", State = QaItemState.Failing, ByName = "Alex", BugReportId = 9 };
            _records["0.4.13/sign-in-logo"] = new QaChecklistItem { Id = 51, ItemKey = "0.4.13/sign-in-logo", State = QaItemState.Passed, ByName = "Sam" };

            var state = Subject.State();

            state.Should().BeEquivalentTo(new { Passed = 1, Failing = 1, Carried = 1 });
            _records.Keys.Should().BeEquivalentTo(Keys("Passing an item", "Failing an item", "Sign-in logo"));
            _records[Key("Sign-in logo")].Id.Should().Be(51, "the same record, under its new key");
            new[] { earlier, report }.Should().OnlyContain(r => r.ChecklistItemKey == Key("Failing an item"), "every report on the item still finds it");
        }

        // What the QA server recorded before the fold: all 27 items passed (November 14, 2026).
        private static readonly string[] BeforeTheFold =
        {
            "0.4.10/dark-theme-icons", "0.4.10/full-height-logo", "0.4.11/logo-on-the-purple-header", "0.4.11/charcoal-arrow", "0.4.12/bot-picture-and-banner",
            "0.4.13/sign-in-logo", "0.4.13/loading-screen", "0.4.15/test-checklist", "0.4.15/passing-an-item", "0.4.15/failing-an-item", "0.4.15/report-a-problem",
            "0.4.15/report-limit", "0.4.16/passes-without-errors", "0.4.16/failing-in-a-row", "0.4.16/member-limit-still-holds", "0.4.16/bug-reports-channel-order",
            "0.4.16/why-a-report-isn-t-on-github", "0.4.16/qa-group", "0.5.0/version-on-the-logo", "0.5.0/version-in-status", "0.5.6/project-links",
            "0.5.6/bug-report-at-the-new-home", "0.6.0/update-card-titles", "0.6.0/admin-channel-update-cards", "0.6.0/read-changes-unchanged",
            "build-4/build-number-on-update-cards", "build-4/build-number-in-the-web-app"
        };

        [TestCase(null, "Update cards", "Read changes")]
        [TestCase("0.4.11/charcoal-arrow", "Update cards", "Read changes", "Web app logo")]
        public void the_fold_keeps_a_merged_items_pass_only_when_every_item_it_came_from_passed(string untested, params string[] toTest)
        {
            foreach (var key in BeforeTheFold.Where(k => k != untested))
            {
                _records[key] = new QaChecklistItem { Id = _nextId++, ItemKey = key, State = QaItemState.Passed, ByName = "EaZyCheaZ", At = new DateTime(2026, 11, 14, 18, 57, 5) };
            }

            Subject.ChecklistText = QaChecklist.Embedded;
            Subject.CurrentVersion = () => new Version(0, 7, 4, 1);
            OnQa("0.3.1,0.3.2,0.3.3,0.3.4");
            var state = Subject.State();

            // The items that came out of the fold (later pull requests add their own, untested).
            var folded = state.Items.Where(i => QaChecklistFold.EarlierKeys.ContainsKey(i.Key) || i.Name is "Update cards" or "Read changes" or "Build number in the web app").ToList();
            folded.Where(i => i.State != "Passed").Select(i => i.Name).Should().Equal(toTest);
            folded.Where(i => i.State == "Passed").Should().OnlyContain(i => i.By == "EaZyCheaZ").And.HaveCount(ItemsFromTheFold - toTest.Length);
            QaChecklistFold.EarlierKeys.Keys.Should().BeSubsetOf(QaChecklist.Parse(QaChecklist.Embedded(), new Version(0, 7, 4)).SelectMany(v => v.Items).Select(i => i.Key),
                "each entry names a folded item by its words; drop an entry once its item is reworded");
        }

        [TestCase(0, 0, "⏳ **1 left to test.**")]
        [TestCase(2, 1, "🔁 **2 carried over** with their pass from an earlier build; **1 new or changed** in this one.\n⏳ **1 left to test.**")]
        public void the_summary_says_how_many_passes_were_carried_over_and_how_many_items_are_new(int carried, int added, string ending)
        {
            QaText.Summary(new[] { new Version(0, 7, 4) }, 3, 2, 0, new QaCarriedOver(carried, added), null, null).Should().EndWith(ending);
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

            await Subject.MarkPassedAsync(Key("Passing an item"), 1, "Alex");
            await Subject.MarkPassedAsync(Key("Failing an item"), 1, "Alex");
            Subject.State().Complete.Should().BeFalse();

            await Subject.MarkPassedAsync(Key("Sign-in logo"), 2, "Sam");
            var state = Subject.State();

            state.Should().BeEquivalentTo(new { Total = 3, Passed = 3, Failing = 0, Complete = true, CompletedBy = "Sam" });
            state.Versions.Should().Equal("0.4.15", "0.4.14", "0.4.13");
            _records[Key("Sign-in logo")].ByDiscordUserId.Should().Be("2");
            _records[Key("Sign-in logo")].At.Should().NotBeNull();
        }

        [Test]
        public async Task a_failing_item_links_its_bug_report_and_blocks_completion_until_it_passes()
        {
            Mocker.GetMock<IBugReportRepository>().Setup(r => r.Find(9)).Returns(new BugReport { Id = 9, IssueUrl = "https://github.com/x/y/issues/1" });

            foreach (var key in Keys("Passing an item", "Sign-in logo"))
            {
                await Subject.MarkPassedAsync(key, 1, "Alex");
            }

            await Subject.MarkFailingAsync(Key("Failing an item"), 1, "Alex", new BugReport { Id = 9 });
            var state = Subject.State();

            state.Should().BeEquivalentTo(new { Passed = 2, Failing = 1, Complete = false });
            state.Items.Single(i => i.Key == Key("Failing an item")).Should().BeEquivalentTo(new { State = "Failing", BugReportId = 9, IssueUrl = "https://github.com/x/y/issues/1" });

            await Subject.MarkPassedAsync(Key("Failing an item"), 2, "Sam");
            Subject.State().Should().BeEquivalentTo(new { Passed = 3, Failing = 0, Complete = true });
        }

        [Test]
        public async Task a_new_promotion_keeps_what_was_recorded_adds_new_items_and_drops_what_staging_has()
        {
            await Subject.MarkPassedAsync(Key("Sign-in logo"), 1, "Alex");
            await Subject.MarkPassedAsync(Key("Passing an item"), 1, "Alex");

            // Staging got 0.4.13, and QA got 0.4.16 with one new item.
            OnQa("0.4.13");
            Subject.ChecklistText = () => Text.Replace("# QA checklist", "# QA checklist\n\n## 0.4.16 (2026-10-26)\n- **New thing:** Do it. You should see it.");
            Subject.CurrentVersion = () => new Version(0, 4, 16, 1);
            var state = Subject.State();

            state.Versions.Should().Equal("0.4.16", "0.4.15", "0.4.14");
            state.Items.Select(i => (i.Name, i.State)).Should().Equal(("New thing", "Pending"), ("Passing an item", "Passed"), ("Failing an item", "Pending"));
        }

        [Test]
        public async Task an_item_no_longer_on_the_checklist_cant_be_marked()
        {
            await Subject.MarkPassedAsync("0.3.0/old", 1, "Alex");

            _records.Should().NotContainKey("0.3.0/old");
        }

        // An item's key in the fixture's checklist, found by its name.
        private static string Key(string name) => QaChecklist.Parse(Text).SelectMany(v => v.Items).Single(i => i.Name == name).Key;

        private static string[] Keys(params string[] names) => names.Select(Key).ToArray();

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
