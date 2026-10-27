using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Requestarr.Core.Announcements
{
    // How a server keeps its "what's new" posts (docs/development.md#version-posts). Set by the environment it
    // runs in; an install that sets nothing is production.
    public enum VersionPostMode
    {
        // Production and staging (beta): every version is posted once, oldest first, and stays for good.
        Standing,

        // QA: every version in QA that staging doesn't have yet. Promoted versions drop off; a reset rebuilds the list.
        NotYetStaged,

        // Dev: only the version running now; a new version replaces the previous post.
        CurrentOnly
    }

    public interface IReleaseEnvironment
    {
        VersionPostMode VersionPosts { get; }

        // QA: the versions staging already has, which QA no longer shows.
        IReadOnlySet<Version> Staged { get; }

        // The web app's icons and logo for this environment: "dev", "qa", "beta" (staging) or "production". The same
        // published image runs on staging and production, so this follows where it runs, not how it was built.
        string IconSet { get; }

        // Staging's beta version of the version it was published as ("0.4.17-beta.1", docs/development.md#versions),
        // or null: every other build, and every other version, shows its plain number.
        string BetaVersion(Version version);
    }

    public class ReleaseEnvironment : IReleaseEnvironment
    {
        // dev, qa or staging; unset is production. Set by the local stacks' compose file and the staging one.
        public const string EnvironmentVariable = "REQUESTARR_ENVIRONMENT";

        // QA only: staging's versions ("0.1.0,0.2.0,..."), from the staging branch's changelog when the QA stack starts.
        public const string StagedVersionsVariable = "REQUESTARR_STAGED_VERSIONS";

        // Staging's image only: the beta version it was published as, set when the image is built.
        public const string ReleaseVersionVariable = "REQUESTARR_RELEASE_VERSION";

        private static readonly Regex Beta = new(@"^(?<version>\d+\.\d+\.\d+)-beta\.[1-9]\d*$");

        public Func<string, string> Variable { get; set; } = Environment.GetEnvironmentVariable;

        public VersionPostMode VersionPosts => (Variable(EnvironmentVariable) ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dev" => VersionPostMode.CurrentOnly,
            "qa" => VersionPostMode.NotYetStaged,
            _ => VersionPostMode.Standing
        };

        public string IconSet => IconSetFor(Variable(EnvironmentVariable));

        // Anything that isn't dev, QA or staging gets production's icons.
        public static string IconSetFor(string environment) => (environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dev" => "dev",
            "qa" => "qa",
            "staging" => "beta",
            _ => "production"
        };

        public string BetaVersion(Version version)
        {
            var match = Beta.Match((Variable(ReleaseVersionVariable) ?? string.Empty).Trim());

            return match.Success && version != null && match.Groups["version"].Value == Changelog.Release(version).ToString() ? match.Value : null;
        }

        public IReadOnlySet<Version> Staged => (Variable(StagedVersionsVariable) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Version.TryParse(v, out var parsed) ? parsed : null)
            .Where(v => v != null)
            .ToHashSet();
    }
}
