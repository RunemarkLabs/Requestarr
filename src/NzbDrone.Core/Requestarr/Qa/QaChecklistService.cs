using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Announcements;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Qa
{
    public record QaChecklistItemState(string Key, string Version, string Name, string State, string By, DateTime? At, int? BugReportId, string IssueUrl);

    // What Claude Code reads from the QA stack before promoting to staging, and what the portal will show.
    public record QaChecklistState(string Environment, IReadOnlyList<string> Versions, int Total, int Passed, int Failing, bool Complete, string CompletedBy, DateTime? CompletedAt, IReadOnlyList<QaChecklistItemState> Items);

    public interface IQaChecklistService
    {
        // Posts the checklist in its channel, or brings it in line with QA-CHECKLIST.md and the record.
        Task EnsureAsync();

        QaChecklistEntry Find(string key);
        QaChecklistState State();

        Task MarkPassedAsync(string key, ulong discordUserId, string name);
        Task MarkFailingAsync(string key, ulong discordUserId, string name, BugReport report);

        // A bug report's issue link arrived: the failing item's card shows it.
        Task RefreshItemAsync(string key);
    }

    // The QA test checklist in Discord: a summary card, then one card per item with Passes and Failing, for every version
    // this server has that staging doesn't (QA), or the running one (dev, where it's tried out). The record is the
    // truth; the cards show it. A new promotion to QA adds the new versions' items (as not tested yet) and drops the
    // versions staging now has; every pass or failure already recorded stays.
    public class QaChecklistService : IQaChecklistService, IHandleAsync<DiscordReadyEvent>
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IQaChecklistItemRepository _items;
        private readonly IBugReportRepository _reports;
        private readonly IReleaseEnvironment _environment;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        public QaChecklistService(IDiscordGuildGateway gateway, IGuildSetupService guildSetup, IDiscordGuildConfigRepository configs, IQaChecklistItemRepository items, IBugReportRepository reports, IReleaseEnvironment environment, IAuditService audit, Logger logger)
        {
            _gateway = gateway;
            _guildSetup = guildSetup;
            _configs = configs;
            _items = items;
            _reports = reports;
            _environment = environment;
            _audit = audit;
            _logger = logger;
        }

        // Settable so tests can use their own checklist and version.
        public Func<string> ChecklistText { get; set; } = QaChecklist.Embedded;
        public Func<Version> CurrentVersion { get; set; } = () => BuildInfo.Version;

        public void HandleAsync(DiscordReadyEvent message)
        {
            EnsureAsync().GetAwaiter().GetResult();
        }

        public IReadOnlyList<QaChecklistVersion> Versions() =>
            QaChecklist.ForServer(QaChecklist.Parse(ChecklistText()), _environment, CurrentVersion());

        public IReadOnlyList<QaChecklistEntry> Entries() => Versions().SelectMany(v => v.Items).ToList();

        public QaChecklistEntry Find(string key) => Entries().FirstOrDefault(e => e.Key == key);

        public QaChecklistState State()
        {
            var entries = Entries();
            var records = Records(entries);
            var reports = records.Values.Where(r => r.BugReportId.HasValue).Select(r => _reports.Find(r.BugReportId.Value)).Where(r => r != null).ToDictionary(r => r.Id);
            var passed = records.Values.Count(r => r.State == QaItemState.Passed);
            var failing = records.Values.Count(r => r.State == QaItemState.Failing);
            var complete = entries.Count > 0 && passed == entries.Count;
            var last = complete ? records.Values.OrderByDescending(r => r.At).First() : null;

            var items = entries.Select(e =>
            {
                var r = records[e.Key];
                var report = r.BugReportId.HasValue && reports.TryGetValue(r.BugReportId.Value, out var found) ? found : null;
                return new QaChecklistItemState(e.Key, e.Version.ToString(), e.Name, r.State.ToString(), r.ByName, r.At, r.BugReportId, report?.IssueUrl);
            }).ToList();

            return new QaChecklistState(_environment.IconSet, Versions().Select(v => v.Version.ToString()).ToList(), entries.Count, passed, failing, complete, last?.ByName, last?.At, items);
        }

        public async Task EnsureAsync()
        {
            if (!QaChecklist.HasChecklist(_environment) || !ulong.TryParse(_guildSetup.GetConfig()?.QaChecklistChannelId, out var channel))
            {
                return;
            }

            await Gate.WaitAsync();

            try
            {
                var entries = Entries();
                var records = Records(entries);
                var config = _guildSetup.GetConfig();

                // The cards, in order: the summary, then each item. Edited in place when they're all still there in
                // that order; otherwise the channel is cleared and posted again (nothing is lost: the record has it).
                var wanted = new List<string> { config.QaChecklistSummaryMessageId }.Concat(entries.Select(e => records[e.Key].MessageId)).ToList();
                var have = (await _gateway.GetOwnMessagesAsync(channel, 500)).Select(m => m.Id.ToString()).ToList();

                if (have.SequenceEqual(wanted))
                {
                    await _gateway.EditMessageAsync(channel, ulong.Parse(wanted[0]), SummaryCard(entries, records));

                    foreach (var entry in entries)
                    {
                        await _gateway.EditMessageAsync(channel, ulong.Parse(records[entry.Key].MessageId), ItemCard(entry, records[entry.Key]));
                    }

                    return;
                }

                foreach (var id in have)
                {
                    await _gateway.DeleteMessageAsync(channel, ulong.Parse(id));
                }

                config.QaChecklistSummaryMessageId = (await _gateway.SendMessageAsync(channel, SummaryCard(entries, records))).ToString();
                _configs.SetFields(config, c => c.QaChecklistSummaryMessageId);

                foreach (var entry in entries)
                {
                    var record = records[entry.Key];
                    record.MessageId = (await _gateway.SendMessageAsync(channel, ItemCard(entry, record))).ToString();
                    _items.SetFields(record, r => r.MessageId);
                }

                _logger.Info("QA checklist posted: {0} item(s) for {1}", entries.Count, string.Join(", ", Versions().Select(v => v.Version)));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not post the QA checklist");
            }
            finally
            {
                Gate.Release();
            }
        }

        public async Task MarkPassedAsync(string key, ulong discordUserId, string name)
        {
            if (Find(key) == null)
            {
                return;
            }

            var wasComplete = State().Complete;
            var record = _items.FindByKey(key);

            record.State = QaItemState.Passed;
            record.ByDiscordUserId = discordUserId.ToString();
            record.ByName = name;
            record.At = DateTime.UtcNow;
            _items.Update(record);
            _audit.Record(Actor.Discord(discordUserId, name), "qa.checklist.pass", "qa-item", key);

            await RefreshItemAsync(key);

            var state = State();

            if (state.Complete && !wasComplete)
            {
                _audit.Record(Actor.Discord(discordUserId, name), "qa.checklist.complete", "qa-checklist", string.Join(",", state.Versions));
                _logger.Info("QA checklist complete for {0}, the last item passed by {1}", string.Join(", ", state.Versions), name);
            }
        }

        public async Task MarkFailingAsync(string key, ulong discordUserId, string name, BugReport report)
        {
            if (Find(key) == null)
            {
                return;
            }

            // Every item on the checklist has a record, even before the cards were posted.
            var record = Records(Entries())[key];

            record.State = QaItemState.Failing;
            record.ByDiscordUserId = discordUserId.ToString();
            record.ByName = name;
            record.At = DateTime.UtcNow;
            record.BugReportId = report?.Id;
            _items.Update(record);
            _audit.Record(Actor.Discord(discordUserId, name), "qa.checklist.fail", "qa-item", key, report != null ? $"bug report {report.Id}" : null);

            await RefreshItemAsync(key);
        }

        public async Task RefreshItemAsync(string key)
        {
            var config = _guildSetup.GetConfig();
            var entry = Find(key);
            var record = _items.FindByKey(key);

            if (entry == null || record?.MessageId == null || !ulong.TryParse(config?.QaChecklistChannelId, out var channel))
            {
                return;
            }

            var entries = Entries();
            await _gateway.EditMessageAsync(channel, ulong.Parse(record.MessageId), ItemCard(entry, record));

            if (ulong.TryParse(config.QaChecklistSummaryMessageId, out var summary))
            {
                await _gateway.EditMessageAsync(channel, summary, SummaryCard(entries, Records(entries)));
            }
        }

        // Every item has a record; new ones start as not tested yet.
        private Dictionary<string, QaChecklistItem> Records(IReadOnlyList<QaChecklistEntry> entries)
        {
            var records = new Dictionary<string, QaChecklistItem>();

            foreach (var entry in entries)
            {
                var record = _items.FindByKey(entry.Key);

                if (record == null)
                {
                    record = _items.Insert(new QaChecklistItem { ItemKey = entry.Key, Version = entry.Version.ToString(), Name = entry.Name, State = QaItemState.Pending });
                }

                records[entry.Key] = record;
            }

            return records;
        }

        private InteractionReply SummaryCard(IReadOnlyList<QaChecklistEntry> entries, Dictionary<string, QaChecklistItem> records)
        {
            var passed = records.Values.Count(r => r.State == QaItemState.Passed);
            var failing = records.Values.Count(r => r.State == QaItemState.Failing);
            var last = entries.Count > 0 && passed == entries.Count ? records.Values.OrderByDescending(r => r.At).First() : null;

            return new InteractionReply
            {
                Quiet = true,
                Cards = { new InteractionCard { Color = SummaryColor(failing, complete: last != null), Text = QaText.Summary(Versions().Select(v => v.Version).ToList(), entries.Count, passed, failing, last?.ByName, last?.At) } }
            };
        }

        private static uint SummaryColor(int failing, bool complete)
        {
            if (failing > 0)
            {
                return QaText.FailingColor;
            }

            return complete ? QaText.PassedColor : QaText.ChecklistColor;
        }

        private InteractionReply ItemCard(QaChecklistEntry entry, QaChecklistItem record)
        {
            var report = record.BugReportId.HasValue ? _reports.Find(record.BugReportId.Value) : null;

            return new InteractionReply
            {
                Quiet = true,
                Cards =
                {
                    new InteractionCard
                    {
                        Color = record.State switch { QaItemState.Passed => QaText.PassedColor, QaItemState.Failing => QaText.FailingColor, _ => QaText.ChecklistColor },
                        Text = QaText.Item(entry, record, report),
                        Buttons = new List<InteractionButton>
                        {
                            new() { CustomId = $"{QaRoutes.Check}:pass:{entry.Key}", Label = QaText.PassLabel, Style = ButtonStyle.Success },
                            new() { CustomId = $"{QaRoutes.Check}:fail:{entry.Key}", Label = QaText.FailLabel, Style = ButtonStyle.Danger }
                        }
                    }
                }
            };
        }
    }
}
