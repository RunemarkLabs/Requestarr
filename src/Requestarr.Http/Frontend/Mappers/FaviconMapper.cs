using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Http.Frontend.Mappers
{
    // /favicon.ico, asked for by browsers on their own: the environment's icon in the theme set in the UI settings
    // (light for Auto, which the server can't see). The pages themselves point at the themed icons.
    public class FaviconMapper : StaticResourceMapperBase
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IReleaseEnvironment _environment;

        public FaviconMapper(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, IConfigFileProvider configFileProvider, IReleaseEnvironment environment, Logger logger)
            : base(diskProvider, logger)
        {
            _appFolderInfo = appFolderInfo;
            _configFileProvider = configFileProvider;
            _environment = environment;
        }

        protected override string FolderPath => Path.Combine(_appFolderInfo.StartUpFolder, _configFileProvider.UiFolder);

        protected override string MapPath(string resourceUrl)
        {
            return Path.Combine(FolderPath, "Content", "Images", "Icons", WebIcons.Theme(_configFileProvider.Theme), _environment.IconSet, "favicon.ico");
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.Equals("/favicon.ico");
        }
    }
}
