using System;
using System.IO;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Http.Frontend.Mappers
{
    public abstract class HtmlMapperBase : StaticResourceMapperBase
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IReleaseEnvironment _environment;
        private readonly Lazy<ICacheBreakerProvider> _cacheBreakProviderFactory;
        private static readonly Regex ReplaceRegex = new Regex(@"(?:(?<attribute>href|src)=\"")(?<path>.*?(?<extension>css|js|png|ico|ics|svg|json))(?:\"")(?:\s(?<nohash>data-no-hash))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private string _urlBase;
        private string _generatedContent;

        protected HtmlMapperBase(IDiskProvider diskProvider,
                                 IConfigFileProvider configFileProvider,
                                 IReleaseEnvironment environment,
                                 Lazy<ICacheBreakerProvider> cacheBreakProviderFactory,
                                 Logger logger)
            : base(diskProvider, logger)
        {
            _diskProvider = diskProvider;
            _configFileProvider = configFileProvider;
            _environment = environment;
            _cacheBreakProviderFactory = cacheBreakProviderFactory;

            _urlBase = configFileProvider.UrlBase;
        }

        protected abstract string HtmlPath { get; }

        protected override Stream GetContentStream(string filePath)
        {
            var text = GetHtmlText();

            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        protected virtual string GetHtmlText()
        {
            if (RuntimeInfo.IsProduction && _generatedContent != null)
            {
                return _generatedContent;
            }

            var text = WebIcons.Apply(_diskProvider.ReadAllText(HtmlPath), _environment, _configFileProvider);
            var cacheBreakProvider = _cacheBreakProviderFactory.Value;

            text = ReplaceRegex.Replace(text, match =>
            {
                string url;

                if (match.Groups["nohash"].Success)
                {
                    url = match.Groups["path"].Value;
                }
                else
                {
                    url = cacheBreakProvider.AddCacheBreakerToPath(match.Groups["path"].Value);
                }

                return $"{match.Groups["attribute"].Value}=\"{_urlBase}{url}\"";
            });

            text = text.Replace("__URL_BASE__", _urlBase);

            _generatedContent = text;

            return _generatedContent;
        }
    }
}
