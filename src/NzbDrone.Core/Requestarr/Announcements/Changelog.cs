using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Requestarr.Core.Announcements
{
    // One "## x.y.z (YYYY-MM-DD)" section of CHANGELOG.md. Users: posted to members in these exact words, and to the
    // admin channel with Admins. A version with nothing for either posts nothing anywhere. Date: the day it was made.
    public record ChangelogVersion(Version Version, IReadOnlyList<string> Users, IReadOnlyList<string> Admins, DateTime? Date = null)
    {
        public bool HasNews => Users.Count + Admins.Count > 0;
    }

    // CHANGELOG.md at the repo root is the source of truth for what changed; it is embedded in the build.
    // Format: "## 0.7.0004 (YYYY-MM-DD)" (up to 0.6.0 "x.y.z"), then "### For users" and "### For admins", each a list of
    // "- " lines; a section with nothing in it is left out, heading included. "## Unreleased" on top holds what changed
    // since the last build sent to testers (docs/development.md#versions). Any other section is ignored here (CI's
    // changelog check refuses it).
    public static class Changelog
    {
        public const string ResourceName = "Requestarr.CHANGELOG.md";
        public const string UsersHeading = "For users";
        public const string AdminsHeading = "For admins";
        public const string UnreleasedHeading = "Unreleased";

        // From 0.7 a version's last part is a running build number people see with four digits; before, x.y.z.
        private static readonly Version FirstBuildNumbered = new(0, 7);

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

        // unreleased: the version a dev or QA build runs as (the next build's number), which its Unreleased lines are
        // read as; null leaves them out, as on staging and production, which only ever run numbered builds.
        public static IReadOnlyList<ChangelogVersion> Parse(string text, Version unreleased = null)
        {
            var versions = new List<ChangelogVersion>();
            Version version = null;
            DateTime? date = null;
            List<string> current = null;
            var sections = new Dictionary<string, List<string>>();

            void Flush()
            {
                if (version != null)
                {
                    versions.Add(new ChangelogVersion(version, Get(UsersHeading), Get(AdminsHeading), date));
                }

                sections = new Dictionary<string, List<string>>();
                current = null;
            }

            List<string> Get(string heading) => sections.TryGetValue(heading, out var lines) ? lines : new List<string>();

            foreach (var raw in (text ?? string.Empty).Split('\n'))
            {
                var line = raw.TrimEnd('\r');

                if (line.StartsWith("## "))
                {
                    Flush();
                    (version, date) = line.Substring(3).Trim() == UnreleasedHeading ? (unreleased, null) : ParseHeading(line.Substring(3));
                }
                else if (line.StartsWith("### ") && version != null)
                {
                    current = new List<string>();
                    sections[line.Substring(4).Trim()] = current;
                }
                else if (line.StartsWith("- ") && current != null)
                {
                    current.Add(line.Substring(2).Trim());
                }
            }

            Flush();

            return versions.OrderByDescending(v => v.Version).ToList();
        }

        // "x.y.z (YYYY-MM-DD)": the version, null when it isn't one, and the day, null when it's missing.
        private static (Version Version, DateTime? Date) ParseHeading(string heading)
        {
            var parts = heading.Trim().Split(' ', 2);
            var version = Version.TryParse(parts[0], out var parsed) ? parsed : null;
            DateTime? date = parts.Length > 1 && DateTime.TryParseExact(parts[1].Trim('(', ')'), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day)
                ? day
                : null;

            return (version, date);
        }

        // The version a build announces: major.minor.build (.NET's revision is left out).
        public static Version Release(Version build) => new(build.Major, build.Minor, Math.Max(build.Build, 0));

        // How people see a version: 0.7.0004 from 0.7 on (.NET reads it as 0.7.4), the old 0.6.0 before.
        public static string Display(Version version)
        {
            var release = Release(version);

            return $"{release.Major}.{release.Minor}.{release.Build.ToString(release >= FirstBuildNumbered ? "D4" : "D", CultureInfo.InvariantCulture)}";
        }
    }

    // The versions a server has announced, stored as "0.2.1,0.2.3,0.2.4". Every version up to the lowest one counts as
    // announced too: a server records only its current version on its first start, and before this list the field
    // held just the highest version announced. Versions don't arrive in order: a fix made in QA takes a number above
    // dev's, and the dev versions it skipped reach QA later.
    public sealed class Announced
    {
        private readonly SortedSet<Version> _versions;

        private Announced(IEnumerable<Version> versions)
        {
            _versions = new SortedSet<Version>(versions);
        }

        public bool IsEmpty => _versions.Count == 0;

        public static Announced Parse(string text)
        {
            var versions = (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => Version.TryParse(v, out var parsed) ? parsed : null)
                .Where(v => v != null);

            return new Announced(versions);
        }

        public bool Contains(Version version) => !IsEmpty && (version <= _versions.Min || _versions.Contains(version));

        public Announced With(IEnumerable<Version> versions) => new(_versions.Concat(versions));

        public override string ToString() => string.Join(",", _versions);
    }
}
