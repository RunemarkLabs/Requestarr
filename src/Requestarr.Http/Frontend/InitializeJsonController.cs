using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Analytics;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Http.Frontend
{
    [Authorize(Policy = "UI")]
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class InitializeJsonController : Controller
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAnalyticsService _analyticsService;
        private readonly IReleaseEnvironment _environment;

        private static string _apiKey;
        private static string _urlBase;
        private string _generatedContent;

        public InitializeJsonController(IConfigFileProvider configFileProvider,
                                      IAnalyticsService analyticsService,
                                      IReleaseEnvironment environment)
        {
            _configFileProvider = configFileProvider;
            _analyticsService = analyticsService;
            _environment = environment;

            _apiKey = configFileProvider.ApiKey;
            _urlBase = configFileProvider.UrlBase;
        }

        [HttpGet("/initialize.json")]
        public IActionResult Index()
        {
            return Content(GetContent(), "application/json");
        }

        private string GetContent()
        {
            if (RuntimeInfo.IsProduction && _generatedContent != null)
            {
                return _generatedContent;
            }

            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine($"  \"apiRoot\": \"{_urlBase}/api/v1\",");
            builder.AppendLine($"  \"apiKey\": \"{_apiKey}\",");
            builder.AppendLine($"  \"release\": \"{BuildInfo.Release}\",");
            builder.AppendLine($"  \"version\": \"{BuildInfo.Version.ToString()}\",");
            builder.AppendLine($"  \"releaseVersion\": \"{_environment.BetaVersion(BuildInfo.Version) ?? Changelog.Release(BuildInfo.Version).ToString()}\",");
            builder.AppendLine($"  \"instanceName\": \"{_configFileProvider.InstanceName.ToString()}\",");
            builder.AppendLine($"  \"theme\": \"{_configFileProvider.Theme.ToString()}\",");
            builder.AppendLine($"  \"iconSet\": \"{_environment.IconSet}\",");
            builder.AppendLine($"  \"branch\": \"{_configFileProvider.Branch.ToLower()}\",");
            builder.AppendLine($"  \"analytics\": {_analyticsService.IsEnabled.ToString().ToLowerInvariant()},");
            builder.AppendLine($"  \"userHash\": \"{HashUtil.AnonymousToken()}\",");
            builder.AppendLine($"  \"urlBase\": \"{_urlBase}\",");
            builder.AppendLine($"  \"isProduction\": {RuntimeInfo.IsProduction.ToString().ToLowerInvariant()}");
            builder.AppendLine("}");

            _generatedContent = builder.ToString();

            return _generatedContent;
        }
    }
}
