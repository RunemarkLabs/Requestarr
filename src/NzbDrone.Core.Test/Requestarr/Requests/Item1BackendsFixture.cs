using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Test.Common;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // Item 1: backends from provisioning, several per type, checked on start, default and per-user instance.
    [TestFixture]
    public class Item1BackendsFixture : RequestScenario
    {
        private IBackendService Backends => Mocker.Resolve<IBackendService>();

        [Test]
        public void provisioning_should_create_several_instances_per_type()
        {
            Backends.All().Select(b => (b.Implementation, b.Name)).Should().BeEquivalentTo(new[] { ("Radarr", "Radarr"), ("Radarr", "Radarr 4K"), ("Sonarr", "Sonarr") });
            Instance("Sonarr").Settings.SeriesType.Should().Be("anime");
            Instance("Radarr").Settings.QualityProfile.Should().Be("HD-1080p");
        }

        [Test]
        public void instances_dropped_from_provisioning_should_be_disabled_not_deleted()
        {
            Backends.SyncFromProvisioning(new BackendsProvisioning { Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr", Url = "http://r", ApiKey = "k" } } });

            Instance("Radarr 4K").Enable.Should().BeFalse();
            Instance("Sonarr").Enable.Should().BeFalse();
            Instance("Radarr").Enable.Should().BeTrue();
        }

        [Test]
        public void default_instance_is_the_marked_one_else_the_first()
        {
            Backends.Default(ContentType.Movie).Name.Should().Be("Radarr");
            Backends.Default(ContentType.Tv).Name.Should().Be("Sonarr");
        }

        [Test]
        public async Task admin_assigns_instance_per_user_and_type()
        {
            var run = await SetAccess(Alice, "movie", "auto", "Radarr 4K");

            run.LastReply.Content.Should().Be(DiscordText.AccessChanged("alice", ContentType.Movie, AccessState.AutoApprove, "Radarr 4K"));

            await RequestMovie(Alice, Dune);
            Radarr.Adds.Single().Url.Should().Be("http://radarr-4k:7878");

            await SetAccess(Bob, "movie", "auto");
            await RequestMovie(Bob, DuneTwo);
            Radarr.Adds.Last().Url.Should().Be("http://radarr-test:7878");
        }

        [Test]
        public async Task assigning_an_unknown_or_wrong_type_instance_should_be_refused()
        {
            (await SetAccess(Alice, "movie", "auto", "Nope")).LastReply.Content.Should().Be(DiscordText.InstanceNotFound("Nope", ContentType.Movie));
            (await SetAccess(Alice, "tv", "auto", "Radarr")).LastReply.Content.Should().Be(DiscordText.InstanceNotFound("Radarr", ContentType.Tv));
        }

        [Test]
        public async Task check_should_pass_for_supported_versions_and_resolve_profile()
        {
            await Backends.CheckAllAsync();

            Backends.LastResults.Should().OnlyContain(r => r.Ok);
            Backends.LastResults.Single(r => r.Name == "Radarr").QualityProfileId.Should().Be(4);
            Mocker.Resolve<BackendHealthCheck>().Check().Type.Should().Be(HealthCheckResult.Ok);
        }

        [TestCase("Sonarr", "3.0.10", "Sonarr 3.0.10 is not supported; version 4 or later is required")]
        [TestCase("Radarr", "4.7.5", "Radarr 4.7.5 is not supported; version 5 or later is required")]
        public async Task check_should_reject_old_versions(string name, string version, string message)
        {
            (name == "Sonarr" ? (FakeArr)Sonarr : Radarr).Version = Version.Parse(version);

            await Backends.CheckAllAsync();

            Backends.LastResults.Single(r => r.Name == name).Error.Should().Be(message);
            var health = Mocker.Resolve<BackendHealthCheck>().Check();
            health.Type.Should().Be(HealthCheckResult.Error);
            health.Message.Should().Contain(message);

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task check_should_report_wrong_app_unreachable_profile_and_root_folder()
        {
            Sonarr.AppName = "Radarr";
            await Backends.CheckAllAsync();
            Backends.LastResults.Single(r => r.Name == "Sonarr").Error.Should().Contain("is Radarr, not Sonarr");

            Sonarr.AppName = "Sonarr";
            Sonarr.Unreachable = true;
            await Backends.CheckAllAsync();
            Backends.LastResults.Single(r => r.Name == "Sonarr").Error.Should().Contain("Connection refused");

            Sonarr.Unreachable = false;
            Sonarr.Profiles.Clear();
            await Backends.CheckAllAsync();
            Backends.LastResults.Single(r => r.Name == "Sonarr").Error.Should().Contain("quality profile 'HD-1080p' not found");

            Radarr.RootFolders.Clear();
            Radarr.RootFolders.Add("/elsewhere");
            await Backends.CheckAllAsync();
            Backends.LastResults.Single(r => r.Name == "Radarr").Error.Should().Contain("root folder '/data/media' not found");

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void provisioning_should_reject_two_defaults_for_one_type()
        {
            var loader = Mocker.Resolve<ProvisioningLoader>();
            loader.EnvironmentLookup = _ => null;

            Action act = () => loader.Parse("backends:\n  radarr:\n    - name: A\n      url: http://a\n      apiKey: k\n      default: true\n    - name: B\n      url: http://b\n      apiKey: k\n      default: true");

            act.Should().Throw<ProvisioningException>().WithMessage("*only one instance can be the default*");
        }
    }
}
