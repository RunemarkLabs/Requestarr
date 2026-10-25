using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Http.Frontend.Mappers
{
    // The install manifest, one per theme (manifest-light.json, manifest-dark.json) with the environment's icons. A
    // browser reads it when the app is installed, so the home screen icon matches the theme showing at that moment.
    public class ManifestMapper : UrlBaseReplacementResourceMapperBase
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IConfigFileProvider _configFileProvider;

        public ManifestMapper(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, IConfigFileProvider configFileProvider, IReleaseEnvironment environment, Logger logger)
            : base(diskProvider, configFileProvider, environment, logger)
        {
            _appFolderInfo = appFolderInfo;
            _configFileProvider = configFileProvider;
        }

        protected override string FolderPath => Path.Combine(_appFolderInfo.StartUpFolder, _configFileProvider.UiFolder);
        protected override string FilePath => Path.Combine(FolderPath, "Content", "manifest-light.json");

        protected override string MapPath(string resourceUrl)
        {
            return Path.Combine(FolderPath, "Content", $"manifest-{WebIcons.ThemeOf(resourceUrl)}.json");
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.StartsWith("/Content/manifest");
        }

        protected override string GetFileText(string filePath)
        {
            return base.GetFileText(filePath).Replace("__INSTANCE_NAME__", _configFileProvider.InstanceName);
        }
    }
}
