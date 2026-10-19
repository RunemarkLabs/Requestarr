using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Test.Provisioning
{
    [TestFixture]
    public class ProvisioningLoaderFixture : CoreTest<ProvisioningLoader>
    {
        private const string FullDocument = @"
version: 1
general:
  instanceName: Requestarr (test)
  logLevel: debug
auth:
  method: forms
  required: disabledForLocalAddresses
  username: admin
  password: ${ADMIN_PASSWORD}
  apiKey: ${API_KEY}
discord:
  botToken: ${DISCORD_BOT_TOKEN:-}
  guildId: '123456789012345678'
backends:
  sonarr:
    - name: Sonarr
      url: http://sonarr:8989
      apiKey: ${SONARR_KEY}
    - name: Sonarr Anime
      url: http://sonarr-anime:8989
      apiKey: ${SONARR_KEY}
      seriesType: anime
  radarr:
    - name: Radarr 4K
      url: https://radarr.example
      apiKey: literal-$${NOT_A_VARIABLE}
mediaServers:
  plex:
    - name: Plex
      url: http://plex:32400
      token: ${PLEX_TOKEN:-placeholder}
";

        private Dictionary<string, string> _environment;

        [SetUp]
        public void Setup()
        {
            _environment = new Dictionary<string, string>
            {
                { "ADMIN_PASSWORD", "p@ss: with # yaml chars" },
                { "API_KEY", "0123456789abcdef0123456789abcdef" },
                { "SONARR_KEY", "sonarr-key" }
            };

            Subject.EnvironmentLookup = name => _environment.GetValueOrDefault(name);
        }

        [Test]
        public void should_parse_all_sections_and_substitute_environment()
        {
            var document = Subject.Parse(FullDocument);

            document.General.InstanceName.Should().Be("Requestarr (test)");
            document.Auth.Password.Should().Be("p@ss: with # yaml chars");
            document.Auth.ApiKey.Should().Be("0123456789abcdef0123456789abcdef");
            document.Discord.BotToken.Should().BeEmpty();
            document.Discord.GuildId.Should().Be("123456789012345678");
            document.Backends.Sonarr.Should().HaveCount(2);
            document.Backends.Sonarr[1].SeriesType.Should().Be("anime");
            document.Backends.Radarr[0].ApiKey.Should().Be("literal-${NOT_A_VARIABLE}");
            document.MediaServers.Plex[0].Token.Should().Be("placeholder");
        }

        [Test]
        public void should_report_missing_environment_variables()
        {
            _environment.Remove("SONARR_KEY");

            Action act = () => Subject.Parse(FullDocument);

            act.Should().Throw<ProvisioningException>().WithMessage("*'SONARR_KEY' is not set*");
        }

        [TestCase("general:\n  instanceName: Something Else", "*must contain 'Requestarr'*")]
        [TestCase("auth:\n  method: magic", "*auth.method must be one of*")]
        [TestCase("auth:\n  method: forms", "*username and auth.password are required*")]
        [TestCase("auth:\n  apiKey: short", "*at least 20 characters*")]
        [TestCase("discord:\n  guildId: not-a-snowflake", "*discord.guildId must be a Discord ID*")]
        [TestCase("backends:\n  sonarr:\n    - name: A\n      url: sonarr:8989\n      apiKey: k", "*url must be an absolute http or https URL*")]
        [TestCase("backends:\n  radarr:\n    - name: A\n      url: http://a\n      apiKey: k\n      seriesType: anime", "*seriesType only applies to sonarr*")]
        [TestCase("backends:\n  sonarr:\n    - name: Same\n      url: http://a\n      apiKey: k\n  radarr:\n    - name: same\n      url: http://b\n      apiKey: k", "*'Same' is used more than once*")]
        [TestCase("version: 2", "*version 2 is not supported*")]
        [TestCase("unknownSection: true", "*not valid*")]
        public void should_reject_invalid_documents(string yaml, string expectedMessage)
        {
            Action act = () => Subject.Parse(yaml);

            act.Should().Throw<ProvisioningException>().WithMessage(expectedMessage);
        }

        [Test]
        public void should_accept_empty_document()
        {
            var document = Subject.Parse(string.Empty);

            document.Version.Should().Be(1);
            document.General.Should().BeNull();
        }

        [Test]
        public void should_use_explicit_path_from_environment()
        {
            var path = Path.Combine(TempFolder, "custom.yml");
            _environment[ProvisioningLoader.PathVariable] = path;

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(path)).Returns(true);

            Subject.ResolvePath().Should().Be(path);
        }

        [Test]
        public void should_fail_when_explicit_path_does_not_exist()
        {
            _environment[ProvisioningLoader.PathVariable] = "/nowhere/provisioning.yml";

            Action act = () => Subject.ResolvePath();

            act.Should().Throw<ProvisioningException>().WithMessage("*does not exist*");
        }

        [Test]
        public void should_fall_back_to_app_data_file_or_none()
        {
            var appData = @"C:\Requestarr\AppData".AsOsAgnostic();
            var expected = Path.Combine(appData, ProvisioningLoader.DefaultFileName);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.AppDataFolder).Returns(appData);

            Subject.ResolvePath().Should().BeNull();

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(expected)).Returns(true);

            Subject.ResolvePath().Should().Be(expected);
        }
    }
}
