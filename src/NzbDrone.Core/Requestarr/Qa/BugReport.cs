using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Qa
{
    // The parts of Requestarr a report can be about. One list, so it grows with the app: the form's choices, the issue's
    // "component:" label and the card all come from here.
    public static class BugReportComponents
    {
        public static readonly IReadOnlyList<(string Key, string Name)> All = new List<(string, string)>
        {
            ("requests", "Requests"),
            ("approvals", "Approvals"),
            ("availability", "Availability and notifications"),
            ("removals", "Removal votes"),
            ("access", "Access and the welcome channel"),
            ("admin", "Admin panel and commands"),
            ("web-app", "Web app"),
            ("other", "Other")
        };

        public static string NameOf(string key) => All.FirstOrDefault(c => c.Key == key).Name ?? "Other";

        public static bool IsKnown(string key) => All.Any(c => c.Key == key);
    }

    public enum BugReportFiling
    {
        // Filing is off on this install (the default for other people's installs).
        Off = 0,
        Filed = 1,
        Failed = 2,

        // On, but this install has no access key for GitHub.
        NoToken = 3
    }

    // One report: what the reporter typed, what Requestarr added (environment, version, time, their Discord name), and
    // the GitHub issue it became. Nothing else from the server goes anywhere.
    public class BugReport : ModelBase
    {
        public string Component { get; set; }
        public string WhatHappened { get; set; }
        public string Expected { get; set; }
        public string Environment { get; set; }
        public string Version { get; set; }
        public string ReporterDiscordId { get; set; }
        public string ReporterName { get; set; }
        public DateTimeOffset Created { get; set; }
        public string ChecklistItemKey { get; set; }
        public BugReportFiling Filing { get; set; }
        public int? IssueNumber { get; set; }
        public string IssueUrl { get; set; }
        public string ChannelId { get; set; }
        public string MessageId { get; set; }
    }

    public interface IBugReportRepository : IBasicRepository<BugReport>
    {
    }

    public class BugReportRepository : BasicRepository<BugReport>, IBugReportRepository
    {
        public BugReportRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    // One row per person: when they last sent reports, so the button can't flood the issue tracker.
    public class BugReportGuard : ModelBase
    {
        public string DiscordUserId { get; set; }
        public string RecentReports { get; set; }

        public List<DateTimeOffset> Reports() => (RecentReports ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => long.TryParse(t, out var ticks) ? new DateTimeOffset(ticks, TimeSpan.Zero) : (DateTimeOffset?)null)
            .Where(t => t != null)
            .Select(t => t.Value)
            .ToList();

        public void SetReports(IEnumerable<DateTimeOffset> reports) => RecentReports = string.Join(",", reports.Select(r => r.UtcTicks));
    }

    public interface IBugReportGuardRepository : IBasicRepository<BugReportGuard>
    {
        BugReportGuard Find(string discordUserId);
    }

    public class BugReportGuardRepository : BasicRepository<BugReportGuard>, IBugReportGuardRepository
    {
        public BugReportGuardRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public BugReportGuard Find(string discordUserId) => Query(g => g.DiscordUserId == discordUserId).SingleOrDefault();
    }

    // The report limit, modelled on the welcome buttons' spam guard: a short wait between reports, and a ceiling per day.
    public static class BugReportLimit
    {
        // How long the person waits before they can send another report; null when they can send one now.
        public static TimeSpan? WaitLeft(IReadOnlyList<DateTimeOffset> reports, DateTimeOffset now, IBugReportSettings settings)
        {
            var recent = reports.Where(r => r > now.AddDays(-1)).OrderBy(r => r).ToList();

            if (recent.Count == 0)
            {
                return null;
            }

            var gap = recent[^1].AddMinutes(settings.MinutesBetween) - now;
            var day = recent.Count >= settings.ReportsPerDay ? recent[^settings.ReportsPerDay].AddDays(1) - now : TimeSpan.Zero;
            var wait = gap > day ? gap : day;

            return wait > TimeSpan.Zero ? wait : null;
        }
    }

    // Bug report settings: kept in Requestarr's settings (the admin web portal reads and changes them there), set by
    // provisioning when it names them. The GitHub access key is in memory only, never in the database or config.
    public interface IBugReportSettings
    {
        // File each report as a GitHub issue. Off unless the admin turns it on (reports become public).
        bool FileIssues { get; }
        string Repository { get; }
        string Token { get; }
        int ReportsPerDay { get; }
        int MinutesBetween { get; }

        void Apply(BugReportsProvisioning provisioning);
    }

    public class BugReportSettings : IBugReportSettings
    {
        public const string DefaultRepository = "RunemarkLabs/Requestarr";
        public const int DefaultReportsPerDay = 5;
        public const int DefaultMinutesBetween = 2;

        private readonly IConfigService _config;

        public BugReportSettings(IConfigService config)
        {
            _config = config;
        }

        public bool FileIssues => _config.BugReportsFileIssues;

        public string Repository => string.IsNullOrWhiteSpace(_config.BugReportsRepository) ? DefaultRepository : _config.BugReportsRepository;

        public string Token { get; private set; }

        public int ReportsPerDay => Math.Max(1, _config.BugReportsPerDay);

        public int MinutesBetween => Math.Max(0, _config.BugReportsMinutesBetween);

        public void Apply(BugReportsProvisioning provisioning)
        {
            Token = string.IsNullOrWhiteSpace(provisioning?.GithubToken) ? null : provisioning.GithubToken.Trim();

            if (provisioning?.FileIssues is { } file)
            {
                _config.BugReportsFileIssues = file;
            }

            if (!string.IsNullOrWhiteSpace(provisioning?.Repository))
            {
                _config.BugReportsRepository = provisioning.Repository.Trim();
            }

            if (provisioning?.ReportsPerDay is { } perDay)
            {
                _config.BugReportsPerDay = Math.Max(1, perDay);
            }

            if (provisioning?.MinutesBetween is { } minutes)
            {
                _config.BugReportsMinutesBetween = Math.Max(0, minutes);
            }
        }
    }
}
