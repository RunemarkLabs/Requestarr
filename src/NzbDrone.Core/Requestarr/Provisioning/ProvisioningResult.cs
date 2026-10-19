using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace Requestarr.Core.Provisioning
{
    public enum ProvisioningOutcome
    {
        NotRun,
        NoFile,
        Succeeded,
        Failed
    }

    public class ProvisioningResult
    {
        public ProvisioningOutcome Outcome { get; private init; }
        public string Path { get; private init; }
        public string Error { get; private init; }
        public List<string> Applied { get; private init; } = new();
        public List<string> Deferred { get; private init; } = new();

        public static ProvisioningResult NotRun() => new() { Outcome = ProvisioningOutcome.NotRun };

        public static ProvisioningResult NoFile() => new() { Outcome = ProvisioningOutcome.NoFile };

        public static ProvisioningResult Failed(string path, string error) => new() { Outcome = ProvisioningOutcome.Failed, Path = path, Error = error };

        public static ProvisioningResult Succeeded(string path, List<string> applied, List<string> deferred) =>
            new() { Outcome = ProvisioningOutcome.Succeeded, Path = path, Applied = applied, Deferred = deferred };
    }

    public class ProvisioningCompletedEvent : IEvent
    {
        public ProvisioningCompletedEvent(ProvisioningResult result)
        {
            Result = result;
        }

        public ProvisioningResult Result { get; }
    }
}
