using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
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

        Task<BugReportResult> SubmitAsync(ulong discordUserId, string reporterName, string component, string whatHappened, string expected, string checklistItemKey);
    }

    // Bug reports from the Report a problem button: kept in Requestarr's record, posted as a card in the bug reports
    // channel, and filed as a GitHub issue when this install files them (its own access key; off by default). A report
    // holds only what the reporter typed plus the environment, version, time, part and their Discord name.
    public class BugReportService : IBugReportService
    {
        public const int FieldLength = 1000;

        private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> Locks = new();

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

        public async Task<BugReportResult> SubmitAsync(ulong discordUserId, string reporterName, string component, string whatHappened, string expected, string checklistItemKey)
        {
            var gate = Locks.GetOrAdd(discordUserId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();

            try
            {
                if (WaitLeft(discordUserId) is { } wait)
                {
                    return new BugReportResult(null, QaText.Wait(wait));
                }

                if (!ulong.TryParse(_guildSetup.GetConfig()?.BugReportsChannelId, out var channel))
                {
                    return new BugReportResult(null, QaText.NoChannel);
                }

                var now = Now();
                var guard = _guards.Find(discordUserId.ToString()) ?? new BugReportGuard { DiscordUserId = discordUserId.ToString() };
                guard.SetReports(guard.Reports().Where(r => r > now.AddDays(-1)).Append(now));
                _guards.Upsert(guard);

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
                    Created = now,
                    ChecklistItemKey = item?.Key,
                    Filing = !_settings.FileIssues ? BugReportFiling.Off : _settings.Token == null ? BugReportFiling.NoToken : BugReportFiling.Failed,
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

                if (_settings.FileIssues && _settings.Token != null)
                {
                    var issue = await _github.CreateAsync(_settings.Repository, _settings.Token, QaText.IssueTitle(report), QaText.IssueBody(report, item), QaText.IssueLabels(report), CancellationToken.None);

                    if (issue != null)
                    {
                        report.Filing = BugReportFiling.Filed;
                        report.IssueNumber = issue.Number;
                        report.IssueUrl = issue.Url;
                        _audit.Record(Actor.System, "bugreport.file", "bug-report", report.Id.ToString(), issue.Url);
                    }

                    _reports.SetFields(report, r => r.Filing, r => r.IssueNumber, r => r.IssueUrl);
                    await _gateway.EditMessageAsync(channel, ulong.Parse(report.MessageId), Card(report));

                    if (item != null)
                    {
                        await _checklist.RefreshItemAsync(item.Key);
                    }
                }

                _logger.Info("Bug report {0} ({1}) from {2}: {3}", report.Id, report.Component, reporterName, QaText.Filing(report));

                return new BugReportResult(report, QaText.Thanks(report, channel));
            }
            finally
            {
                gate.Release();
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
