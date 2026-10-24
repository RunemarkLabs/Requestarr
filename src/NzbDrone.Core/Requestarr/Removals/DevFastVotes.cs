using System;
using System.IO;
using NzbDrone.Common.EnvironmentInfo;

namespace Requestarr.Core.Removals
{
    // Dev stack only: removal timings in minutes, so a vote closing, the requester's reminder and
    // silence-as-yes can be watched by hand. On while a marker file is in Requestarr's data folder
    // (docker/dev.ps1 fast-votes on), and only when the container runs as the dev stack
    // (REQUESTARR_DEV_STACK, which only docker/compose.yaml sets). reset-uat wipes the data folder,
    // so a reset is always back to the real settings.
    public interface IDevFastVotes
    {
        bool On { get; }
    }

    public class DevFastVotes : IDevFastVotes
    {
        public const string MarkerFile = "dev-fast-votes";
        public const string DevStackVariable = "REQUESTARR_DEV_STACK";

        public static readonly TimeSpan VoteWindow = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan RequesterTimeout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan Reminder = TimeSpan.FromMinutes(3);

        private readonly IAppFolderInfo _folders;

        public DevFastVotes(IAppFolderInfo folders)
        {
            _folders = folders;
        }

        // Settable for tests.
        public Func<string, string> Variable { get; set; } = Environment.GetEnvironmentVariable;

        public bool DevStack => string.Equals(Variable(DevStackVariable), "true", StringComparison.OrdinalIgnoreCase);

        public bool On => DevStack && File.Exists(Path.Combine(_folders.AppDataFolder, MarkerFile));
    }
}
