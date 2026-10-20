using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.AnimeDub
{
    // Items 4-6: separate sub and dub instances, Anime numbering, and help.
    [TestFixture]
    public class Item4To6Fixture : AnimeScenario
    {
        public static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")) && !File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        }

        private void TwoSonarrs()
        {
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr", Url = "http://radarr-test:7878", ApiKey = "k", QualityProfile = "HD-1080p", RootFolder = "/data/media", Default = true } },
                Sonarr = new List<BackendInstanceProvisioning>
                {
                    new() { Name = "Sonarr", Url = "http://sonarr-test:8989", ApiKey = "k", QualityProfile = "HD-1080p", RootFolder = "/data/media", Default = true },
                    new() { Name = "Sonarr Sub", Url = "http://sonarr-sub:8989", ApiKey = "k", QualityProfile = "HD-1080p", RootFolder = "/data/anime-sub", Anime = new() { Priority = "sub" } }
                }
            });
            Sonarr.RootFolders.Add("/data/anime-sub");
        }

        // Item 4
        [Test]
        public async Task separate_sub_and_dub_instances_each_get_their_own_series()
        {
            TwoSonarrs();
            await SetAccess(Bob, "tv", "auto", "Sonarr Sub");

            await RequestAnime(Alice, "d");
            await RequestAnime(Bob, "s");

            Sonarr.Adds.Select(a => (a.Url, a.RootFolder)).Should().BeEquivalentTo(new[] { ("http://sonarr-test:8989", "/data/media"), ("http://sonarr-sub:8989", "/data/anime-sub") });
            RequestStore.All().Select(r => r.Language).Should().BeEquivalentTo(new AnimeLanguage?[] { AnimeLanguage.Dub, AnimeLanguage.Sub });
            Instance("Sonarr Sub").Settings.AnimePriority.Should().Be("sub");
        }

        [Test]
        public void the_setup_guide_warns_about_separate_instances()
        {
            var guide = File.ReadAllText(RepoFile("docs", "setup-guide.md"));

            guide.Should().Contain("different root folders").And.Contain("appears twice");
        }

        // Item 5
        [Test]
        public async Task anime_is_always_added_as_anime_and_other_tv_uses_the_instances_type()
        {
            var repo = Mocker.Resolve<IBackendRepository>();
            var instance = SonarrInstance;
            instance.Settings.SeriesType = "standard";
            repo.Update(instance);

            await RequestAnime(Alice, "d");
            await Click(Bob, $"req-tv-all:{Severance}");

            Sonarr.Adds.Single(a => a.Item.TvdbId == OnePiece).SeriesType.Should().Be("anime");
            Sonarr.Adds.Single(a => a.Item.TvdbId == Severance).SeriesType.Should().Be("standard");
        }

        [Test]
        public async Task anime_stays_anime_type_with_sub_dub_turned_off_and_gets_no_tag()
        {
            Priority("dub", enabled: false);

            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{OnePiece}"))).LastReply;
            card.Buttons.Select(b => b.CustomId).Should().Contain($"req-tv-all:{OnePiece}");
            await Click(Alice, $"req-tv-all:{OnePiece}");

            Sonarr.Adds.Single().SeriesType.Should().Be("anime");
            Sonarr.Adds.Single().Tags.Should().BeEmpty();
            RequestFor().Language.Should().BeNull();
        }

        [Test]
        public async Task a_series_the_admin_switched_to_standard_is_never_switched_back()
        {
            var tags = await SetUp();
            var id = Sonarr.AddToLibrary(OnePiece, "standard", tags.Sub);

            await AskLanguage(Bob, "d");

            Sonarr.SeriesTypes[id].Should().Be("standard");
            Sonarr.SeriesTags[id].Should().Equal(tags.Dub);
        }

        // Item 6
        [Test]
        public async Task users_with_tv_get_the_anime_help_topic()
        {
            var help = await Harness.SendAsync(UserCommand("help", Alice));
            help.LastReply.SelectMenu.Options.Select(o => o.Value).Should().Contain("anime");

            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "anime").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            topic.LastReply.Content.Should().StartWith("**Anime: sub or dub**").And.Contain("dub wins, sub wins, or the first request decides").And.Contain("episodes already downloaded stay");

            await SetAccess(Alice, "tv", "off");
            (await Harness.SendAsync(UserCommand("help", Alice))).LastReply.SelectMenu.Options.Select(o => o.Value).Should().NotContain("anime");
        }

        [Test]
        public async Task admins_get_the_anime_setup_and_priority_topic()
        {
            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "anime").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            topic.LastReply.Content.Should().StartWith("**Anime, sub/dub and priority**")
                .And.Contain(Requestarr.Core.Anime.AnimeDefaults.DubTag).And.Contain(Requestarr.Core.Anime.AnimeDefaults.SubProfile)
                .And.Contain("anime.priority").And.Contain("different root folders");
            HelpCatalog.AdminTopics.Select(t => t.Key).Should().Contain("anime");
        }
    }
}
