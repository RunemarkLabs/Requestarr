using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Requestarr.Core.Announcements
{
    // One "## x.y.z" section of CHANGELOG.md. Users: posted to members in these exact words.
    // Admins and Internal: posted to the admin channel only.
    public record ChangelogVersion(Version Version, IReadOnlyList<string> Users, IReadOnlyList<string> Admins, IReadOnlyList<string> Internal);

    // CHANGELOG.md at the repo root is the source of truth for what changed; it is embedded in the build.
    // Format: "## x.y.z", then "### For users", "### For admins" and "### Internal", each a list of "- " lines.
    public static class Changelog
    {
        public const string ResourceName = "Requestarr.CHANGELOG.md";
        public const string UsersHeading = "For users";
        public const string AdminsHeading = "For admins";
        public const string InternalHeading = "Internal";

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
            List<string> current = null;
            var sections = new Dictionary<string, List<string>>();

            void Flush()
            {
                if (version != null)
                {
                    versions.Add(new ChangelogVersion(version, Get(UsersHeading), Get(AdminsHeading), Get(InternalHeading)));
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
                    version = Version.TryParse(line.Substring(3).Trim(), out var parsed) ? parsed : null;
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
}
