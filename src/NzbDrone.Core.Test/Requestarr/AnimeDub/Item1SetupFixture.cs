using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Anime;
using Requestarr.Core.Backends;

namespace Requestarr.Core.Test.AnimeDub
{
    // Item 1: Requestarr sets up Sonarr for sub/dub itself: clearly its own, idempotent, and
    // never touching what the Sonarr owner made.
    [TestFixture]
    public class Item1SetupFixture : AnimeScenario
    {
        [Test]
        public async Task setup_creates_two_tags_and_two_tagged_release_profiles()
        {
            var result = await Anime.EnsureAsync(SonarrInstance);

            result.Ok.Should().BeTrue();
            result.TagsCreated.Should().Be(2);
            result.ProfilesCreated.Should().Be(2);
            Sonarr.Tags.Select(t => t.Name).Should().Equal(AnimeDefaults.DubTag, AnimeDefaults.SubTag);

            var dub = Sonarr.ReleaseProfiles.Single(p => p.Name == AnimeDefaults.DubProfile);
            var sub = Sonarr.ReleaseProfiles.Single(p => p.Name == AnimeDefaults.SubProfile);
            dub.Tags.Should().Equal(result.Tags.Dub);
            dub.Required.Should().Equal(AnimeDefaults.DubRequired);
            dub.Ignored.Should().BeEmpty();
            sub.Tags.Should().Equal(result.Tags.Sub);
            sub.Required.Should().BeEmpty();
            sub.Ignored.Should().Equal(AnimeDefaults.SubIgnored);
            sub.Ignored.Should().NotContain(t => t.Contains("Dual"), "dual-audio keeps the original audio and subtitles");
        }

        [Test]
        public async Task running_setup_again_creates_and_changes_nothing()
        {
            await Anime.EnsureAsync(SonarrInstance);
            var writes = Sonarr.Writes;

            var again = await Anime.EnsureAsync(SonarrInstance);

            again.TagsCreated.Should().Be(0);
            again.ProfilesCreated.Should().Be(0);
            again.ProfilesUpdated.Should().Be(0);
            Sonarr.Writes.Should().Be(writes);
            Sonarr.Tags.Should().HaveCount(2);
            Sonarr.ReleaseProfiles.Should().HaveCount(2);
        }

        [Test]
        public async Task the_owners_tags_and_profiles_are_never_touched()
        {
            Sonarr.Tags.Add(new NamedItem(40, "dub"));
            Sonarr.Tags.Add(new NamedItem(41, "anime"));
            Sonarr.ReleaseProfiles.Add(new ReleaseProfile { Id = 7, Name = "My anime preferences", Required = { "1080p" }, Tags = { 41 } });

            await Anime.EnsureAsync(SonarrInstance);

            Sonarr.Tags.Should().ContainEquivalentOf(new NamedItem(40, "dub")).And.ContainEquivalentOf(new NamedItem(41, "anime"));
            Sonarr.Tags.Select(t => t.Name).Should().Contain(new[] { AnimeDefaults.DubTag, AnimeDefaults.SubTag });
            var owners = Sonarr.ReleaseProfiles.Single(p => p.Id == 7);
            owners.Name.Should().Be("My anime preferences");
            owners.Required.Should().Equal("1080p");
            owners.Tags.Should().Equal(41);
            Sonarr.ReleaseProfiles.Should().HaveCount(3);
        }

        [Test]
        public async Task release_terms_come_from_provisioning_and_ours_follow_changes()
        {
            var repo = Mocker.Resolve<IBackendRepository>();
            var instance = SonarrInstance;
            instance.Settings.DubRequired = new() { "Dual Audio", "English Dub" };
            repo.Update(instance);
            await Anime.EnsureAsync(SonarrInstance);
            Sonarr.ReleaseProfiles.Single(p => p.Name == AnimeDefaults.DubProfile).Required.Should().Equal("Dual Audio", "English Dub");

            instance = SonarrInstance;
            instance.Settings.DubRequired = new() { "Dual Audio" };
            repo.Update(instance);
            var result = await Anime.EnsureAsync(SonarrInstance);

            result.ProfilesUpdated.Should().Be(1);
            Sonarr.ReleaseProfiles.Single(p => p.Name == AnimeDefaults.DubProfile).Required.Should().Equal("Dual Audio");
            Sonarr.ReleaseProfiles.Should().HaveCount(2);
        }

        [Test]
        public async Task an_instance_that_opts_out_gets_nothing_created()
        {
            Priority("dub", enabled: false);

            var result = await Anime.EnsureAsync(SonarrInstance);

            result.Tags.Should().BeNull();
            Sonarr.Tags.Should().BeEmpty();
            Sonarr.ReleaseProfiles.Should().BeEmpty();
        }

        [Test]
        public async Task setup_runs_for_every_sonarr_that_passes_its_check()
        {
            await Mocker.Resolve<IBackendService>().CheckAllAsync();

            await Anime.SetUpAllAsync();

            Sonarr.ReleaseProfiles.Select(p => p.Name).Should().BeEquivalentTo(AnimeDefaults.DubProfile, AnimeDefaults.SubProfile);
        }

        [Test]
        public void provisioning_defaults_and_validation()
        {
            AnimeDefaults.ParsePriority(null).Should().Be(AnimePriority.Dub);
            AnimeDefaults.ParsePriority("SUB").Should().Be(AnimePriority.Sub);
            AnimeDefaults.ParsePriority("first").Should().Be(AnimePriority.First);
            new BackendSettings().AnimeEnabled.Should().BeTrue();

            var errors = Requestarr.Core.Provisioning.ProvisioningLoader.Validate(new Requestarr.Core.Provisioning.ProvisioningDocument
            {
                Backends = new()
                {
                    Sonarr = { new() { Name = "S", Url = "http://s:8989", ApiKey = "x", Anime = new() { Priority = "loudest" } } },
                    Radarr = { new() { Name = "R", Url = "http://r:7878", ApiKey = "x", Anime = new() { Priority = "dub" } } }
                }
            }).ToList();

            errors.Should().Contain(e => e.Contains("anime.priority must be one of: dub, sub, first"));
            errors.Should().Contain(e => e.Contains("anime only applies to sonarr"));
        }
    }
}
