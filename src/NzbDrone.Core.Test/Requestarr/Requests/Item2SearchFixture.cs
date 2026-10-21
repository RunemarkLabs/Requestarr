using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // Item 2: search by query or ID through the assigned instance's own lookup; detail card.
    [TestFixture]
    public class Item2SearchFixture : RequestScenario
    {
        [Test]
        public async Task movie_search_should_list_results_in_a_select_menu()
        {
            var run = await Harness.SendAsync(Search(Alice, "movie", "dune"));

            run.LastReply.Content.Should().Contain(DiscordText.SearchResults(ContentType.Movie, "dune", 2));
            var menu = run.LastReply.SelectMenu;
            menu.CustomId.Should().Be("req-pick:movie:dune");
            menu.Options.Select(o => (o.Label, o.Value)).Should().Equal(("Dune (2021)", Dune.ToString()), ("Dune: Part Two (2024)", DuneTwo.ToString()));
        }

        [Test]
        public async Task results_should_respect_discord_limits()
        {
            for (var i = 0; i < 40; i++)
            {
                Radarr.Catalog.Add(new Backends.MediaLookupResult { Title = "Long " + new string('x', 150) + i, TmdbId = 1000 + i, Overview = new string('o', 300) });
            }

            var run = await Harness.SendAsync(Search(Alice, "movie", "long"));

            var menu = run.LastReply.SelectMenu;
            menu.Options.Count.Should().Be(RequestService.MaxSearchResults);
            menu.Options.Should().OnlyContain(o => o.Label.Length <= DiscordLimits.OptionLabelLength && o.Description.Length <= DiscordLimits.OptionLabelLength);
        }

        [Test]
        public async Task picking_a_result_should_show_detail_card_with_poster()
        {
            var run = await Harness.SendAsync(FakeInteraction.SelectMenu("req-pick:movie", Dune.ToString()).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            var card = run.LastReply;
            card.Embeds.Single().Title.Should().Be("Dune (2021)");
            card.Embeds.Single().Description.Should().Be("Paul Atreides...");
            card.Embeds.Single().ImageUrl.Should().Be("https://image.example/dune.jpg");
            card.Buttons.Single().CustomId.Should().Be($"req-make:movie:{Dune}");
        }

        [Test]
        public async Task request_by_tmdb_and_tvdb_id_should_show_detail_directly()
        {
            (await Harness.SendAsync(Search(Alice, "movie", "tmdb:" + Dune))).LastReply.Embeds.Single().Title.Should().Be("Dune (2021)");
            (await Harness.SendAsync(Search(Alice, "tv", "tvdb:" + Severance))).LastReply.Embeds.Single().Title.Should().Be("Severance (2022)");
        }

        [Test]
        public async Task unknown_id_and_empty_search_should_explain()
        {
            (await Harness.SendAsync(Search(Alice, "movie", "tmdb:999"))).LastReply.Content.Should().Contain(DiscordText.TitleNotFound);
            (await Harness.SendAsync(Search(Alice, "movie", "zzz"))).LastReply.Content.Should().Contain(DiscordText.NothingFound);
        }

        [Test]
        public async Task unreachable_backend_should_be_reported()
        {
            Radarr.Unreachable = true;

            (await Harness.SendAsync(Search(Alice, "movie", "dune"))).LastReply.Content.Should().Contain(DiscordText.RequestsPaused(ContentType.Movie, "Radarr"));

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task searching_in_someone_elses_channel_should_be_refused()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("request").WithOption("type", "movie").WithOption("query", "dune").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Bob)));

            run.LastReply.Content.Should().Contain(DiscordText.OwnChannelOnly);
        }

        [Test]
        public async Task music_is_not_requestable_yet()
        {
            var caller = new RequestCaller(UserFor(Alice), false, Audit.Actor.System, "alice");

            (await Requests.SearchAsync(caller, ContentType.Music, "x")).Error.Should().Be(DiscordText.MusicNotYet);
        }
    }
}
