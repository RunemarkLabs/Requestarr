using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Analytics
{
    public interface IAnalyticsService
    {
        bool IsEnabled { get; }
        bool InstallIsActive { get; }
    }

    public class AnalyticsService : IAnalyticsService
    {
        private readonly IConfigFileProvider _configFileProvider;

        public AnalyticsService(IConfigFileProvider configFileProvider)
        {
            _configFileProvider = configFileProvider;
        }

        public bool IsEnabled => (_configFileProvider.AnalyticsEnabled && RuntimeInfo.IsProduction) || RuntimeInfo.IsDevelopment;

        // Prowlarr derived this from indexer history; Requestarr has no equivalent activity record yet.
        public bool InstallIsActive => true;
    }
}
