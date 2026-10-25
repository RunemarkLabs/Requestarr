using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Http.Frontend.Mappers
{
    // Windows tile settings, one per theme (browserconfig-light.xml, browserconfig-dark.xml) with the environment's
    // tiles. Windows reads them when the site is pinned.
    public class BrowserConfig : UrlBaseReplacementResourceMapperBase
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IConfigFileProvider _configFileProvider;

        public BrowserConfig(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, IConfigFileProvider configFileProvider, IReleaseEnvironment environment, Logger logger)
            : base(diskProvider, configFileProvider, environment, logger)
        {
            _appFolderInfo = appFolderInfo;
            _configFileProvider = configFileProvider;
        }

        protected override string FolderPath => Path.Combine(_appFolderInfo.StartUpFolder, _configFileProvider.UiFolder);
        protected override string FilePath => Path.Combine(FolderPath, "Content", "browserconfig-light.xml");

        protected override string MapPath(string resourceUrl)
        {
            return Path.Combine(FolderPath, "Content", $"browserconfig-{WebIcons.ThemeOf(resourceUrl)}.xml");
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.StartsWith("/Content/browserconfig");
        }
    }
}
