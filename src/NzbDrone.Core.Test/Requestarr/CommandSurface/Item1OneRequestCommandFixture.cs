using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.CommandSurface
{
    // Item 1: /request {type} {query}; type autocompleted from the caller's own access.
    [TestFixture]
    public class Item1OneRequestCommandFixture : RequestScenario
    {
        private async Task<string[]> Suggest(ulong user, string typed = "", ulong? channel = null)
        {
            var run = await Harness.SendAsync(FakeInteraction.Autocomplete("request", "type", typed).By(user).InGuild(FakeDiscordGuild.Guild).InChannel(channel ?? ChannelOf(user)));
            run.Responder.Responses.Single().Kind.Should().Be(ResponseKind.Suggestions);
            return run.Responder.Suggestions.Select(s => s.Value).ToArray();
        }

        [Test]
        public async Task autocomplete_offers_only_types_the_user_can_request()
        {
            (await Suggest(Alice)).Should().Equal("movie", "tv");

            await SetAccess(Alice, "tv", "off");
            (await Suggest(Alice)).Should().Equal("movie");

            await SetAccess(Alice, "movie", "off");
            (await Suggest(Alice)).Should().BeEmpty();
        }

        [Test]
        public async Task autocomplete_never_offers_music_comics_or_books()
        {
            await SetAccess(Alice, "music", "auto");

            (await Suggest(Alice)).Should().NotContain("music");
        }

        [Test]
        public async Task autocomplete_filters_by_what_was_typed()
        {
            (await Suggest(Alice, "mo")).Should().Equal("movie");
            (await Suggest(Alice, "tv")).Should().Equal("tv");
        }

        [Test]
        public async Task autocomplete_for_admin_in_admin_channel_offers_types_with_a_backend()
        {
            var run = await Harness.SendAsync(FakeInteraction.Autocomplete("request", "type", "").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.Responder.Suggestions.Select(s => s.Value).Should().Equal("movie", "tv");
        }

        [Test]
        public async Task autocomplete_offers_nothing_to_people_who_cannot_request_here()
        {
            (await Suggest(Alice, channel: ChannelOf(Bob))).Should().BeEmpty();
            (await Suggest(Moderator, channel: ChannelOf(Alice))).Should().BeEmpty();
        }

        [Test]
        public async Task request_searches_by_title()
        {
            var run = await Harness.SendAsync(Search(Alice, "movie", "dune"));

            run.LastReply.SelectMenu.Options.Select(o => o.Value).Should().Equal(Dune.ToString(), DuneTwo.ToString());
        }

        [Test]
        public async Task request_by_tmdb_and_tvdb_id()
        {
            (await Harness.SendAsync(Search(Alice, "movie", "tmdb:603"))).LastReply.Content.Should().Be(DiscordText.TitleNotFound);
            (await Harness.SendAsync(Search(Alice, "movie", $"tmdb:{Dune}"))).LastReply.Embeds.Single().Title.Should().Be("Dune (2021)");
            (await Harness.SendAsync(Search(Alice, "tv", $"TVDB: {Severance}"))).LastReply.Embeds.Single().Title.Should().Be("Severance (2022)");
        }

        [Test]
        public async Task id_of_the_wrong_kind_is_explained()
        {
            (await Harness.SendAsync(Search(Alice, "tv", $"tmdb:{Dune}"))).LastReply.Content.Should().Be(DiscordText.TmdbIsForMovies);
            (await Harness.SendAsync(Search(Alice, "movie", $"tvdb:{Severance}"))).LastReply.Content.Should().Be(DiscordText.TvdbIsForTv);
        }

        [Test]
        public async Task typed_types_are_rechecked_server_side()
        {
            await SetAccess(Alice, "movie", "off");

            (await Harness.SendAsync(Search(Alice, "movie", "dune"))).LastReply.Content.Should().Be(DiscordText.AccessOff(ContentType.Movie));
            (await Harness.SendAsync(Search(Alice, "music", "x"))).LastReply.Content.Should().Be(DiscordText.MusicNotYet);
            (await Harness.SendAsync(Search(Alice, "books", "x"))).LastReply.Content.Should().Be(DiscordText.ChooseType);
        }

        [Test]
        public async Task full_request_flow_still_works_behind_the_new_command()
        {
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{Severance}"))).LastReply;
            card.SelectMenu.CustomId.Should().Be($"req-seasons:{Severance}");

            (await RequestTv(Alice, Severance, "s1", "future")).LastReply.Content.Should().Be(DiscordText.RequestPending("Severance (2022)", 1));
            (await RequestTv(Bob, Severance, "s1")).LastReply.Content.Should().Be(DiscordText.Subscribed("Severance (2022)", "alice", 1));
            (await AdminButton("req-approve:1")).LastReply.Content.Should().Contain("approved");
            Sonarr.Adds.Single().MonitorFuture.Should().BeTrue();
        }
    }
}
