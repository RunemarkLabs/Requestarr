using System;
using System.Collections.Concurrent;
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
    public record BugReportResult(BugReport Report, string Message);

    public interface IBugReportService
    {
        // How long the person waits before they may send a report; null when they can send one now.
        TimeSpan? WaitLeft(ulong discordUserId);

        bool FilesIssues { get; }

        // limited: held to the report limit (members' Report a problem); an admin failing checklist items isn't.
        Task<BugReportResult> SubmitAsync(ulong discordUserId, string reporterName, string component, string whatHappened, string expected, string checklistItemKey, bool limited = true);

        // Every report not on GitHub yet is filed now if it can be, without its reporter sending it again; each card
        // (and its checklist item) then says where it is, or exactly why it isn't.
        Task RetryUnfiledAsync();
    }

    // Bug reports from the Report a problem button: kept in Requestarr's record, posted as a card in the bug reports
    // channel, and filed as a GitHub issue when this install files them (its own access key; off by default, always on
    // for the test servers). A report holds only what the reporter typed plus the environment, version, time, part and
    // their Discord name. One that couldn't be filed is tried again each time the bot connects.
    public class BugReportService : IBugReportService, IHandleAsync<DiscordReadyEvent>
    {
        public const int FieldLength = 1000;

        private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> Locks = new();

        // One filing at a time, so a report is never filed twice (a retry while it's being sent, say).
        private static readonly SemaphoreSlim Filing = new(1, 1);

        private readonly IBugReportRepository _reports;
        private readonly IBugReportGuardRepository _guards;
        private readonly IBugReportSettings _settings;
        private readonly IGitHubIssues _github;
        private readonly IQaChecklistService _checklist;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IReleaseEnvironment _environment;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        public BugReportService(IBugReportRepository reports, IBugReportGuardRepository guards, IBugReportSettings settings, IGitHubIssues github, IQaChecklistService checklist, IDiscordGuildGateway gateway, IGuildSetupService guildSetup, IReleaseEnvironment environment, IAuditService audit, Logger logger)
        {
            _reports = reports;
            _guards = guards;
            _settings = settings;
            _github = github;
            _checklist = checklist;
            _gateway = gateway;
            _guildSetup = guildSetup;
            _environment = environment;
            _audit = audit;
            _logger = logger;
        }

        public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

        public bool FilesIssues => _settings.FileIssues;

        public TimeSpan? WaitLeft(ulong discordUserId) =>
            BugReportLimit.WaitLeft(_guards.Find(discordUserId.ToString())?.Reports() ?? new(), Now(), _settings);

        public void HandleAsync(DiscordReadyEvent message)
        {
            try
            {
                RetryUnfiledAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not file the bug reports that aren't on GitHub yet");
            }
        }

        public async Task<BugReportResult> SubmitAsync(ulong discordUserId, string reporterName, string component, string whatHappened, string expected, string checklistItemKey, bool limited = true)
        {
            var gate = Locks.GetOrAdd(discordUserId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();

            try
            {
                if (limited && WaitLeft(discordUserId) is { } wait)
                {
                    return new BugReportResult(null, QaText.Wait(wait));
                }

                if (!ulong.TryParse(_guildSetup.GetConfig()?.BugReportsChannelId, out var channel))
                {
                    return new BugReportResult(null, QaText.NoChannel);
                }

                var now = Now();

                if (limited)
                {
                    var guard = _guards.Find(discordUserId.ToString()) ?? new BugReportGuard { DiscordUserId = discordUserId.ToString() };
                    guard.SetReports(guard.Reports().Where(r => r > now.AddDays(-1)).Append(now));
                    _guards.Upsert(guard);
                }

                var item = checklistItemKey != null ? _checklist.Find(checklistItemKey) : null;
                var report = _reports.Insert(new BugReport
                {
                    Component = BugReportComponents.IsKnown(component) ? component : "other",
                    WhatHappened = Trim(whatHappened),
                    Expected = Trim(expected),
                    Environment = _environment.IconSet,
                    Version = Changelog.Release(BuildInfo.Version).ToString(),
                    ReporterDiscordId = discordUserId.ToString(),
                    ReporterName = reporterName,
                    Created = now.UtcDateTime,
                    ChecklistItemKey = item?.Key,
                    Filing = CanFile() ?? BugReportFiling.Pending,
                    ChannelId = channel.ToString()
                });

                _audit.Record(Actor.Discord(discordUserId, reporterName), "bugreport.send", "bug-report", report.Id.ToString(), report.Component);

                // Posted first, so the report is never lost to GitHub being slow or down.
                report.MessageId = (await _gateway.SendMessageAsync(channel, Card(report))).ToString();
                _reports.SetFields(report, r => r.MessageId);

                if (item != null)
                {
                    await _checklist.MarkFailingAsync(item.Key, discordUserId, reporterName, report);
                }

                if (report.Filing == BugReportFiling.Pending)
                {
                    report = await FileAsync(report.Id) ?? report;
                }

                _logger.Info("Bug report {0} ({1}) from {2}: {3}", report.Id, report.Component, reporterName, QaText.Filing(report));

                return new BugReportResult(report, QaText.Thanks(report, channel));
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task RetryUnfiledAsync()
        {
            foreach (var id in _reports.All().Where(r => r.Filing != BugReportFiling.Filed).OrderBy(r => r.Id).Select(r => r.Id).ToList())
            {
                var report = await FileAsync(id);

                if (report?.Filing == BugReportFiling.Filed)
                {
                    _logger.Info("Bug report {0} is on GitHub now: {1}", report.Id, report.IssueUrl);
                }
            }
        }

        // Why a report can't be filed now (filing off, or no access key), or null when it can.
        private BugReportFiling? CanFile() => !_settings.FileIssues ? BugReportFiling.Off : _settings.Token == null ? BugReportFiling.NoToken : null;

        // Files one report if it isn't on GitHub yet, or records why it can't be; its card (and its checklist item)
        // follow when that changed. Returns the report as it is now.
        private async Task<BugReport> FileAsync(int id)
        {
            await Filing.WaitAsync();

            try
            {
                var report = _reports.Find(id);

                if (report == null || report.Filing == BugReportFiling.Filed)
                {
                    return report;
                }

                var before = (report.Filing, report.FilingProblem);

                if (CanFile() is { } cannot)
                {
                    report.Filing = cannot;
                    report.FilingProblem = null;
                }
                else
                {
                    var item = report.ChecklistItemKey != null ? _checklist.Find(report.ChecklistItemKey) : null;
                    var result = await _github.CreateAsync(_settings.Repository, _settings.Token, QaText.IssueTitle(report), QaText.IssueBody(report, item), QaText.IssueLabels(report), CancellationToken.None);

                    if (result?.Issue != null)
                    {
                        report.Filing = BugReportFiling.Filed;
                        report.FilingProblem = null;
                        report.IssueNumber = result.Issue.Number;
                        report.IssueUrl = result.Issue.Url;
                        _audit.Record(Actor.System, "bugreport.file", "bug-report", report.Id.ToString(), result.Issue.Url);
                    }
                    else
                    {
                        report.Filing = BugReportFiling.Failed;
                        report.FilingProblem = result?.Problem ?? "GitHub gave no answer";
                    }
                }

                if ((report.Filing, report.FilingProblem) == before)
                {
                    return report;
                }

                _reports.SetFields(report, r => r.Filing, r => r.FilingProblem, r => r.IssueNumber, r => r.IssueUrl);

                if (ulong.TryParse(report.ChannelId, out var channel) && ulong.TryParse(report.MessageId, out var message))
                {
                    await _gateway.EditMessageAsync(channel, message, Card(report));
                }

                if (report.ChecklistItemKey != null)
                {
                    await _checklist.RefreshItemAsync(report.ChecklistItemKey);
                }

                return report;
            }
            finally
            {
                Filing.Release();
            }
        }

        private static InteractionReply Card(BugReport report) => new()
        {
            Quiet = true,
            Cards = { new InteractionCard { Color = QaText.ReportColor, Text = QaText.ReportCard(report) } }
        };

        private static string Trim(string text)
        {
            text = (text ?? string.Empty).Trim();
            return text.Length > FieldLength ? text.Substring(0, FieldLength) : text;
        }
    }
}
