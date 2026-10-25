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
    // Format: "## x.y.z (YYYY-MM-DD)", then "### For users" and "### For admins", each a list of "- " lines; a section with
    // nothing in it is left out, heading included. Any other section is ignored here (CI's changelog check refuses it).
    public static class Changelog
    {
        public const string ResourceName = "Requestarr.CHANGELOG.md";
        public const string UsersHeading = "For users";
        public const string AdminsHeading = "For admins";

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

        public static IReadOnlyList<ChangelogVersion> Parse(string text)
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
                    var heading = line.Substring(3).Trim().Split(' ', 2);
                    version = Version.TryParse(heading[0], out var parsed) ? parsed : null;
                    date = heading.Length > 1 && DateTime.TryParseExact(heading[1].Trim('(', ')'), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day)
                        ? day
                        : null;
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

        // The version a build announces: major.minor.build (the revision is the build number).
        public static Version Release(Version build) => new(build.Major, build.Minor, Math.Max(build.Build, 0));
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
