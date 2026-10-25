using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Announcements;

namespace Requestarr.Core.Qa
{
    // One thing to test in a version: "- **Name:** what to do. You should see what happens." Key: "0.4.15/name-in-words",
    // stable across releases, so a pass is kept when the checklist is posted again.
    public record QaChecklistEntry(Version Version, string Key, string Name, string Text);

    public record QaChecklistVersion(Version Version, IReadOnlyList<QaChecklistEntry> Items);

    // QA-CHECKLIST.md at the repo root is what admins on the QA server test before a version goes to staging; every pull
    // request adds its version's items (CI's checklist check enforces the format). It is embedded in the build.
    public static class QaChecklist
    {
        public const string ResourceName = "Requestarr.QA-CHECKLIST.md";

        private static readonly Regex Heading = new(@"^## (?<version>\d+\.\d+\.\d+)(?: \(\d{4}-\d{2}-\d{2}\))?\s*$");
        private static readonly Regex Item = new(@"^- \*\*(?<name>[^*:]+):\*\* (?<text>.+)$");

        public static string Embedded()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);

            if (stream == null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static IReadOnlyList<QaChecklistVersion> Parse(string text)
        {
            var versions = new List<QaChecklistVersion>();
            Version current = null;
            List<QaChecklistEntry> items = null;

            foreach (var raw in (text ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                var heading = Heading.Match(line);

                if (heading.Success)
                {
                    current = Version.Parse(heading.Groups["version"].Value);
                    items = new List<QaChecklistEntry>();
                    versions.Add(new QaChecklistVersion(current, items));
                    continue;
                }

                var item = Item.Match(line);

                if (current != null && item.Success)
                {
                    var name = item.Groups["name"].Value.Trim();
                    items.Add(new QaChecklistEntry(current, $"{current}/{Slug(name)}", name, item.Groups["text"].Value.Trim()));
                }
            }

            return versions;
        }

        // The versions this server tests: QA every version staging doesn't have yet; dev only the one running (where the
        // checklist is tried out). Other servers have no checklist.
        public static IReadOnlyList<QaChecklistVersion> ForServer(IReadOnlyList<QaChecklistVersion> versions, IReleaseEnvironment environment, Version running)
        {
            var current = Changelog.Release(running);

            return environment.VersionPosts switch
            {
                VersionPostMode.NotYetStaged => versions.Where(v => v.Version <= current && !environment.Staged.Contains(v.Version)).ToList(),
                VersionPostMode.CurrentOnly => versions.Where(v => v.Version == current).ToList(),
                _ => new List<QaChecklistVersion>()
            };
        }

        public static bool HasChecklist(IReleaseEnvironment environment) => environment.VersionPosts != VersionPostMode.Standing;

        private static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }

    public enum QaItemState
    {
        Pending = 0,
        Passed = 1,
        Failing = 2
    }

    // Requestarr's record of each checklist item on this server: who passed or failed it and when, and the bug report
    // a failing one opened. Discord shows it; the portal will too.
    public class QaChecklistItem : ModelBase
    {
        public string ItemKey { get; set; }
        public string Version { get; set; }
        public string Name { get; set; }
        public QaItemState State { get; set; }
        public string ByDiscordUserId { get; set; }
        public string ByName { get; set; }
        public DateTimeOffset? At { get; set; }
        public int? BugReportId { get; set; }
        public string MessageId { get; set; }
    }

    public interface IQaChecklistItemRepository : IBasicRepository<QaChecklistItem>
    {
        QaChecklistItem FindByKey(string itemKey);
    }

    public class QaChecklistItemRepository : BasicRepository<QaChecklistItem>, IQaChecklistItemRepository
    {
        public QaChecklistItemRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public QaChecklistItem FindByKey(string itemKey) => Query(i => i.ItemKey == itemKey).SingleOrDefault();
    }
}
