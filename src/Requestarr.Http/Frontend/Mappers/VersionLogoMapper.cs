using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace Requestarr.Http.Frontend.Mappers
{
    // /Content/Images/Icons/{light|light-purple|dark}/production/logo-version.svg: the production header and login logo
    // with the running version on its bar (VersionLogo); light-purple is the light theme's purple header. Made from the
    // delivered R the first time it's asked for; never cached by the browser, so an upgrade shows the new version
    // straight away.
    public class VersionLogoMapper : IMapHttpRequestsToDisk
    {
        public const string FileName = "logo-version.svg";

        private static readonly Regex Url = new(@"^/content/images/icons/(light|light-purple|dark)/production/logo-version\.svg$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<string, string> Made = new();

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly Logger _logger;

        public VersionLogoMapper(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, IConfigFileProvider configFileProvider, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _configFileProvider = configFileProvider;
            _logger = logger;
        }

        public static bool Handles(string resourceUrl) => Url.IsMatch(resourceUrl);

        public bool CanHandle(string resourceUrl) => Handles(resourceUrl);

        // The delivered R it's drawn beside.
        public string Map(string resourceUrl)
        {
            var theme = Url.Match(resourceUrl).Groups[1].Value.ToLowerInvariant();

            return Path.Combine(_appFolderInfo.StartUpFolder, _configFileProvider.UiFolder, "Content", "Images", "Icons", theme, "production", "logo-header.png");
        }

        public Task<IActionResult> GetResponse(string resourceUrl)
        {
            var logo = Map(resourceUrl);

            if (!_diskProvider.FileExists(logo))
            {
                _logger.Warn("File {0} not found", logo);

                return Task.FromResult<IActionResult>(null);
            }

            var svg = Made.GetOrAdd(logo, path => VersionLogo.Svg(BuildInfo.Version, File.ReadAllBytes(path)));

            return Task.FromResult<IActionResult>(new ContentResult { Content = svg, ContentType = "image/svg+xml" });
        }
    }
}
