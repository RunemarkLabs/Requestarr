using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.Polish
{
    // Item 2: a large poster and every useful field Sonarr and Radarr return.
    [TestFixture]
    public class Item2CardFixture : PolishScenario
    {
        [Test]
        public async Task movie_card_has_a_large_poster_and_every_field()
        {
            var card = (await Harness.SendAsync(Search(Alice, "movie", $"tmdb:{Dune}"))).LastReply;
            var embed = card.Embeds.Single();

            embed.ImageUrl.Should().Be("https://image.example/dune.jpg");
            embed.ThumbnailUrl.Should().BeNull();
            embed.Title.Should().Be("Dune (2021)");
            embed.Url.Should().Be("https://www.imdb.com/title/tt1160419/");
            embed.Description.Should().Be("Paul Atreides...");
            FieldNames(card).Should().Equal("Genres", "Rating", "Rated", "Runtime", "Studio", "Status", "Released", "Goes to");
            Field(card, "Genres").Should().Be("Science Fiction, Adventure");
            Field(card, "Rating").Should().Be("IMDb 8.0 · TMDB 7.8 · Rotten Tomatoes 83%");
            Field(card, "Rated").Should().Be("PG-13");
            Field(card, "Runtime").Should().Be("2 h 35 min");
            Field(card, "Studio").Should().Be("Legendary Pictures");
            Field(card, "Status").Should().Be("Released");
            Field(card, "Released").Should().Be("In cinemas 15 Sep 2021\nDigital 21 Oct 2021\nPhysical 22 Dec 2021");
            Field(card, "Goes to").Should().Be("Radarr");
            embed.Footer.Should().BeNull();
        }

        [Test]
        public async Task tv_card_has_network_status_seasons_and_air_dates()
        {
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;

            card.Embeds.Single().ImageUrl.Should().Be("https://image.example/dark-matter-2024.jpg");
            FieldNames(card).Should().Equal("Genres", "Rating", "Rated", "Runtime", "Network", "Status", "Seasons", "Aired", "Goes to");
            Field(card, "Rating").Should().Be("7.6 (83,949 votes)");
            Field(card, "Runtime").Should().Be("54 min per episode");
            Field(card, "Network").Should().Be("Apple TV");
            Field(card, "Status").Should().Be("Continuing");
            Field(card, "Seasons").Should().Be("2 + specials");
            Field(card, "Aired").Should().Be("First 8 May 2024");
            Field(card, "Goes to").Should().Be("Sonarr");
        }

        [Test]
        public async Task ended_show_shows_first_and_last_air_dates()
        {
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter2015}"))).LastReply;

            Field(card, "Status").Should().Be("Ended");
            Field(card, "Aired").Should().Be("First 12 Jun 2015\nLast 25 Aug 2017");
            Field(card, "Seasons").Should().Be("3");
        }

        [Test]
        public async Task with_the_key_the_card_lists_top_cast_and_episodes_with_attribution()
        {
            TmdbKey.Set("test-key");

            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;

            Field(card, "Cast").Should().Be("Joel Edgerton, Jennifer Connelly, Alice Braga");
            Field(card, "Episodes").Should().Be("9");
            card.Embeds.Single().Footer.Should().Be(DiscordText.TmdbAttribution);
        }

        [Test]
        public async Task a_title_with_few_details_still_gets_a_card()
        {
            var card = (await Harness.SendAsync(Search(Alice, "movie", $"tmdb:{DuneTwo}"))).LastReply;

            card.Embeds.Single().Title.Should().Be("Dune: Part Two (2024)");
            FieldNames(card).Should().Equal("Goes to");
        }

        [Test]
        public void approval_cards_use_the_compact_card()
        {
            var item = Radarr.Catalog.First();
            var embed = RequestCards.Approval(new Request { Id = 1, Title = item.Title, Year = item.Year, ContentType = Requestarr.Core.Users.ContentType.Movie }, "alice", "Radarr", item).Embeds.Single();

            embed.ThumbnailUrl.Should().Be(item.PosterUrl);
            embed.ImageUrl.Should().BeNull();
        }
    }
}
