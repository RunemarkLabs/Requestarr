using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Requestarr.Core.Provisioning
{
    public interface IProvisioningLoader
    {
        string ResolvePath();
        ProvisioningDocument Load(string path);
        ProvisioningDocument Parse(string yaml);
    }

    public class ProvisioningLoader : IProvisioningLoader
    {
        public const string PathVariable = "REQUESTARR_PROVISIONING_FILE";
        public const string DefaultFileName = "provisioning.yml";

        private static readonly string[] AuthMethods = { "none", "basic", "forms", "external" };
        private static readonly string[] AuthRequired = { "enabled", "disabledforlocaladdresses" };
        private static readonly string[] LogLevels = { "info", "debug", "trace" };
        private static readonly string[] SeriesTypes = { "standard", "anime", "daily" };

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;

        public ProvisioningLoader(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
        }

        // Replaceable for tests.
        public Func<string, string> EnvironmentLookup { get; set; } = Environment.GetEnvironmentVariable;

        // Explicit path from the environment wins; otherwise provisioning.yml in the app data folder, if present.
        public string ResolvePath()
        {
            var explicitPath = EnvironmentLookup(PathVariable);

            if (explicitPath.IsNotNullOrWhiteSpace())
            {
                if (!_diskProvider.FileExists(explicitPath))
                {
                    throw new ProvisioningException($"{PathVariable} points to '{explicitPath}', which does not exist");
                }

                return explicitPath;
            }

            var defaultPath = Path.Combine(_appFolderInfo.AppDataFolder, DefaultFileName);

            return _diskProvider.FileExists(defaultPath) ? defaultPath : null;
        }

        public ProvisioningDocument Load(string path)
        {
            return Parse(_diskProvider.ReadAllText(path));
        }

        public ProvisioningDocument Parse(string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            ProvisioningDocument document;

            try
            {
                document = deserializer.Deserialize<ProvisioningDocument>(yaml) ?? new ProvisioningDocument();
            }
            catch (YamlException ex)
            {
                throw new ProvisioningException($"Provisioning file is not valid: {ex.Message}{(ex.InnerException != null ? " " + ex.InnerException.Message : string.Empty)}");
            }

            // Substitute after parsing so secret values never affect YAML syntax.
            var missing = new List<string>();
            SubstituteStrings(document, missing);

            var errors = missing.Distinct().Select(v => $"Environment variable '{v}' is not set and has no default").ToList();
            errors.AddRange(Validate(document));

            if (errors.Any())
            {
                throw new ProvisioningException("Provisioning file has errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => " - " + e)));
            }

            return document;
        }

        private void SubstituteStrings(object target, List<string> missing)
        {
            if (target == null)
            {
                return;
            }

            foreach (var property in target.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite))
            {
                var value = property.GetValue(target);

                switch (value)
                {
                    case string text:
                        property.SetValue(target, EnvironmentSubstitution.Substitute(text, EnvironmentLookup, missing));
                        break;
                    case List<string> list:
                        for (var i = 0; i < list.Count; i++)
                        {
                            list[i] = EnvironmentSubstitution.Substitute(list[i], EnvironmentLookup, missing);
                        }

                        break;
                    case IList items:
                        foreach (var item in items)
                        {
                            SubstituteStrings(item, missing);
                        }

                        break;
                    case not null when property.PropertyType.IsClass && property.PropertyType.Namespace == typeof(ProvisioningDocument).Namespace:
                        SubstituteStrings(value, missing);
                        break;
                }
            }
        }

        private static IEnumerable<string> Validate(ProvisioningDocument document)
        {
            if (document.Version != 1)
            {
                yield return $"version {document.Version} is not supported (expected 1)";
            }

            var general = document.General;

            if (general != null)
            {
                if (general.InstanceName.IsNotNullOrWhiteSpace() && !general.InstanceName.ContainsIgnoreCase(BuildInfo.AppName))
                {
                    yield return $"general.instanceName must contain '{BuildInfo.AppName}'";
                }

                if (general.LogLevel.IsNotNullOrWhiteSpace() && !LogLevels.Contains(general.LogLevel.ToLowerInvariant()))
                {
                    yield return $"general.logLevel must be one of: {string.Join(", ", LogLevels)}";
                }

                if (general.UrlBase.IsNotNullOrWhiteSpace() && !general.UrlBase.StartsWith("/"))
                {
                    yield return "general.urlBase must start with '/'";
                }
            }

            var auth = document.Auth;

            if (auth != null)
            {
                if (auth.Method.IsNotNullOrWhiteSpace() && !AuthMethods.Contains(auth.Method.ToLowerInvariant()))
                {
                    yield return $"auth.method must be one of: {string.Join(", ", AuthMethods)}";
                }

                if (auth.Required.IsNotNullOrWhiteSpace() && !AuthRequired.Contains(auth.Required.ToLowerInvariant()))
                {
                    yield return "auth.required must be 'enabled' or 'disabledForLocalAddresses'";
                }

                var method = auth.Method?.ToLowerInvariant();

                if ((method == "basic" || method == "forms") && (auth.Username.IsNullOrWhiteSpace() || auth.Password.IsNullOrWhiteSpace()))
                {
                    yield return $"auth.username and auth.password are required when auth.method is '{auth.Method}'";
                }

                if (auth.Username.IsNotNullOrWhiteSpace() != auth.Password.IsNotNullOrWhiteSpace())
                {
                    yield return "auth.username and auth.password must be set together";
                }

                if (auth.ApiKey.IsNotNullOrWhiteSpace() && auth.ApiKey.Length < 20)
                {
                    yield return "auth.apiKey must be at least 20 characters";
                }
            }

            var discord = document.Discord;

            if (discord != null)
            {
                foreach (var (field, value) in new[]
                         {
                             ("applicationId", discord.ApplicationId),
                             ("guildId", discord.GuildId),
                             ("adminRoleId", discord.AdminRoleId),
                             ("adminChannelId", discord.AdminChannelId),
                             ("adminQueueChannelId", discord.AdminQueueChannelId)
                         })
                {
                    if (value.IsNotNullOrWhiteSpace() && !ulong.TryParse(value, out _))
                    {
                        yield return $"discord.{field} must be a Discord ID (digits only)";
                    }
                }
            }

            if (document.Backends != null)
            {
                var all = new List<(string Type, BackendInstanceProvisioning Instance)>();
                all.AddRange(document.Backends.Sonarr.Select(i => ("sonarr", i)));
                all.AddRange(document.Backends.Radarr.Select(i => ("radarr", i)));
                all.AddRange(document.Backends.Lidarr.Select(i => ("lidarr", i)));

                foreach (var (type, instance) in all)
                {
                    var label = $"backends.{type} '{instance.Name}'";

                    if (instance.Name.IsNullOrWhiteSpace())
                    {
                        yield return $"backends.{type}: every instance needs a name";
                    }

                    if (!IsHttpUrl(instance.Url))
                    {
                        yield return $"{label}: url must be an absolute http or https URL";
                    }

                    if (instance.ApiKey.IsNullOrWhiteSpace())
                    {
                        yield return $"{label}: apiKey is required";
                    }

                    if (instance.SeriesType.IsNotNullOrWhiteSpace())
                    {
                        if (type != "sonarr")
                        {
                            yield return $"{label}: seriesType only applies to sonarr";
                        }
                        else if (!SeriesTypes.Contains(instance.SeriesType.ToLowerInvariant()))
                        {
                            yield return $"{label}: seriesType must be one of: {string.Join(", ", SeriesTypes)}";
                        }
                    }
                }

                foreach (var duplicate in all.Where(i => i.Instance.Name.IsNotNullOrWhiteSpace())
                             .GroupBy(i => i.Instance.Name, StringComparer.OrdinalIgnoreCase)
                             .Where(g => g.Count() > 1))
                {
                    yield return $"backends: instance name '{duplicate.Key}' is used more than once";
                }
            }

            if (document.MediaServers != null)
            {
                foreach (var (type, server) in document.MediaServers.Plex.Select(s => ("plex", s))
                             .Concat(document.MediaServers.Jellyfin.Select(s => ("jellyfin", s))))
                {
                    if (server.Name.IsNullOrWhiteSpace())
                    {
                        yield return $"mediaServers.{type}: every server needs a name";
                    }

                    if (!IsHttpUrl(server.Url))
                    {
                        yield return $"mediaServers.{type} '{server.Name}': url must be an absolute http or https URL";
                    }
                }
            }
        }

        private static bool IsHttpUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
