using System.Collections.Concurrent;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Http.Frontend.Mappers
{
    public abstract class UrlBaseReplacementResourceMapperBase : StaticResourceMapperBase
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IReleaseEnvironment _environment;
        private readonly string _urlBase;
        private readonly ConcurrentDictionary<string, string> _generatedContent = new();

        public UrlBaseReplacementResourceMapperBase(IDiskProvider diskProvider, IConfigFileProvider configFileProvider, IReleaseEnvironment environment, Logger logger)
            : base(diskProvider, logger)
        {
            _diskProvider = diskProvider;
            _configFileProvider = configFileProvider;
            _environment = environment;
            _urlBase = configFileProvider.UrlBase;
        }

        protected abstract string FilePath { get; }

        protected override string MapPath(string resourceUrl)
        {
            return FilePath;
        }

        protected override Stream GetContentStream(string filePath)
        {
            var text = GetFileText(filePath);

            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        // Each file (one per theme) is read once in production, with the URL base and the icon set filled in.
        protected virtual string GetFileText(string filePath)
        {
            if (RuntimeInfo.IsProduction && _generatedContent.TryGetValue(filePath, out var cached))
            {
                return cached;
            }

            var text = _diskProvider.ReadAllText(filePath);

            text = WebIcons.Apply(text.Replace("__URL_BASE__", _urlBase), _environment, _configFileProvider, WebIcons.ThemeOf(Path.GetFileName(filePath)));

            _generatedContent[filePath] = text;

            return text;
        }
    }
}
