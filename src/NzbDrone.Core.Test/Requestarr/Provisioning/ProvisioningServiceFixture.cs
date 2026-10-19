using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Test.Provisioning
{
    [TestFixture]
    public class ProvisioningServiceFixture : CoreTest<ProvisioningService>
    {
        private const string Path = "/config/provisioning.yml";

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDiscordSettingsStore>(new DiscordSettingsStore());
        }

        private void GivenDocument(ProvisioningDocument document)
        {
            Mocker.GetMock<IProvisioningLoader>().Setup(l => l.ResolvePath()).Returns(Path);
            Mocker.GetMock<IProvisioningLoader>().Setup(l => l.Load(Path)).Returns(document);
        }

        [Test]
        public void should_do_nothing_without_a_file()
        {
            Subject.Apply().Outcome.Should().Be(ProvisioningOutcome.NoFile);

            Mocker.GetMock<IConfigFileProvider>().Verify(c => c.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()), Times.Never());
        }

        [Test]
        public void should_apply_general_and_auth_settings()
        {
            GivenDocument(new ProvisioningDocument
            {
                General = new GeneralProvisioning { InstanceName = "Requestarr (test)", LogLevel = "Debug" },
                Auth = new AuthProvisioning
                {
                    Method = "forms",
                    Required = "disabledForLocalAddresses",
                    Username = "admin",
                    Password = "secret",
                    ApiKey = "0123456789abcdef0123456789abcdef"
                }
            });

            var result = Subject.Apply();

            result.Outcome.Should().Be(ProvisioningOutcome.Succeeded);

            Mocker.GetMock<IConfigFileProvider>().Verify(c => c.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d => IsExpectedSettings(d))), Times.Once());

            Mocker.GetMock<IUserService>().Verify(u => u.Upsert("admin", "secret"), Times.Once());
            Mocker.GetMock<IConfigFileProvider>().Verify(c => c.SetApiKey("0123456789abcdef0123456789abcdef"), Times.Once());
        }

        private static bool IsExpectedSettings(Dictionary<string, object> d)
        {
            return (string)d["InstanceName"] == "Requestarr (test)" &&
                   (string)d["LogLevel"] == "debug" &&
                   (AuthenticationType)d["AuthenticationMethod"] == AuthenticationType.Forms &&
                   (AuthenticationRequiredType)d["AuthenticationRequired"] == AuthenticationRequiredType.DisabledForLocalAddresses;
        }

        [Test]
        public void should_leave_unset_keys_untouched()
        {
            GivenDocument(new ProvisioningDocument { General = new GeneralProvisioning { InstanceName = "Requestarr" } });

            Subject.Apply();

            Mocker.GetMock<IConfigFileProvider>().Verify(c => c.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d => d.Count == 1)), Times.Once());
            Mocker.GetMock<IUserService>().Verify(u => u.Upsert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IConfigFileProvider>().Verify(c => c.SetApiKey(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_report_unbuilt_sections_as_deferred()
        {
            GivenDocument(new ProvisioningDocument
            {
                Discord = new DiscordProvisioning(),
                Backends = new BackendsProvisioning
                {
                    Sonarr = new List<BackendInstanceProvisioning> { new(), new() },
                    Radarr = new List<BackendInstanceProvisioning> { new() }
                },
                MediaServers = new MediaServersProvisioning()
            });

            var result = Subject.Apply();

            result.Deferred.Should().BeEquivalentTo("backends (sonarr x2, radarr x1)", "mediaServers");
            result.Applied.Should().Contain("discord (no bot token, bot not started)");
        }

        [Test]
        public void should_record_failure_and_raise_health_check()
        {
            Mocker.GetMock<IProvisioningLoader>().Setup(l => l.ResolvePath()).Throws(new ProvisioningException("bad file"));

            Subject.Apply().Outcome.Should().Be(ProvisioningOutcome.Failed);

            Mocker.SetConstant<IProvisioningService>(Subject);
            var check = Mocker.Resolve<ProvisioningCheck>().Check();

            check.Type.Should().Be(NzbDrone.Core.HealthCheck.HealthCheckResult.Error);
            check.Message.Should().Contain("bad file");

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
