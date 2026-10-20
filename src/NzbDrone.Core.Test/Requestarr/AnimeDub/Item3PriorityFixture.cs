using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.AnimeDub
{
    // Item 3: one language per series per instance; the instance's priority decides who wins.
    [TestFixture]
    public class Item3PriorityFixture : AnimeScenario
    {
        private IRequestSubscriberRepository Subscribers => Mocker.Resolve<IRequestSubscriberRepository>();

        [SetUp]
        public async Task PendingForAlice()
        {
            await SetAccess(Alice, "tv", "approval");
        }

        [Test]
        public async Task dub_wins_a_dub_request_switches_a_pending_sub_request_and_tells_its_owner()
        {
            var alices = await RequestAnime(Alice, "s");

            var run = await AskLanguage(Bob, "d");

            RequestFor().Language.Should().Be(AnimeLanguage.Dub);
            Subscribers.ForRequest(RequestFor().Id).Select(s => s.UserId).Should().Contain(UserFor(Bob).Id);
            run.LastReply.Content.Should().StartWith(DiscordText.FinalFollowing).And.Contain("now set to **Dub**")
                .And.Contain(DiscordText.LanguageLine(AnimeLanguage.Dub)).And.NotContain(DiscordText.LanguageLine(AnimeLanguage.Sub));

            // Alice's message is reposted at the bottom of her channel with the honest note.
            ChannelFor(Alice).MessageIds.Should().NotContain(alices);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalPending}")
                .And.Contain(DiscordText.LanguageLine(AnimeLanguage.Dub))
                .And.Contain("Episodes already downloaded stay as they are");
            Latest(Alice).Buttons.Select(b => b.CustomId).Should().Equal(new[] { $"req-cancel:{RequestFor().Id}", $"req-add:{RequestFor().Id}" }, "a pending request keeps its Cancel (and TV can add seasons)");
            Mocker.Resolve<IAuditRepository>().All().Should().Contain(a => a.Action == "anime.switch");
        }

        [Test]
        public async Task dub_wins_a_sub_request_for_a_dub_series_follows_it()
        {
            await RequestAnime(Alice, "d");

            var run = await AskLanguage(Bob, "s");

            RequestFor().Language.Should().Be(AnimeLanguage.Dub);
            run.LastReply.Content.Should().StartWith(DiscordText.FinalFollowing).And.Contain("stays **Dub** (dub wins here)");
            Subscribers.ForRequest(RequestFor().Id).Should().ContainSingle();
        }

        [Test]
        public async Task sub_wins_reverses_it()
        {
            Priority("sub");
            await RequestAnime(Alice, "d");

            await AskLanguage(Bob, "s");

            RequestFor().Language.Should().Be(AnimeLanguage.Sub);
        }

        [Test]
        public async Task first_request_wins_keeps_the_first_language()
        {
            Priority("first");
            await RequestAnime(Alice, "s");

            var run = await AskLanguage(Bob, "d");

            RequestFor().Language.Should().Be(AnimeLanguage.Sub);
            run.LastReply.Content.Should().Contain("stays **Sub** (the first request decides here)");
        }

        [Test]
        public async Task in_the_library_the_winning_language_swaps_the_sonarr_tag_and_keeps_the_owners_tags()
        {
            var tags = await SetUp();
            var id = Sonarr.AddToLibrary(OnePiece, "anime", 41, tags.Sub);

            var run = await AskLanguage(Bob, "d");

            Sonarr.SeriesTags[id].Should().BeEquivalentTo(new[] { 41, tags.Dub });
            run.LastReply.Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain("now set to **Dub**").And.Contain("episodes already downloaded stay as they are");
            RequestStore.All().Should().BeEmpty();
        }

        // Found live: a sent (approved) request whose series is now in Sonarr is good news, like any
        // title in the library, and the switch shows the new language everywhere.
        [Test]
        public async Task switching_a_series_that_was_sent_and_is_in_the_library_is_good_news()
        {
            var tags = await SetUp();
            await SetAccess(Alice, "tv", "auto");
            await RequestAnime(Alice, "s");
            var id = Sonarr.SeriesTags.Keys.Single();

            var run = await AskLanguage(Bob, "d");

            run.LastReply.Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain("now set to **Dub**").And.NotContain(DiscordText.LanguageLine(AnimeLanguage.Sub));
            Sonarr.SeriesTags[id].Should().Equal(tags.Dub);
            RequestFor().Language.Should().Be(AnimeLanguage.Dub);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}>").And.Contain(DiscordText.LanguageLine(AnimeLanguage.Dub)).And.Contain("Now set to **Dub**");
            Subscribers.ForRequest(RequestFor().Id).Should().BeEmpty("it's in the library: nothing to follow");
        }

        [Test]
        public async Task in_the_library_the_losing_language_changes_nothing()
        {
            var tags = await SetUp();
            var id = Sonarr.AddToLibrary(OnePiece, "anime", tags.Dub);

            var run = await AskLanguage(Bob, "s");

            Sonarr.SeriesTags[id].Should().Equal(tags.Dub);
            run.LastReply.Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain("set to **Dub** (dub wins here)");
        }

        [Test]
        public async Task a_library_series_requestarr_never_tagged_is_left_alone()
        {
            await SetUp();
            var id = Sonarr.AddToLibrary(OnePiece, "anime", 41);

            var run = await AskLanguage(Bob, "d");

            Sonarr.SeriesTags[id].Should().Equal(41);
            run.LastReply.Content.Should().StartWith(DiscordText.FinalAvailable);
        }

        [Test]
        public async Task a_switch_tells_every_follower_and_the_owner_but_not_the_one_who_switched()
        {
            await AddUser(Moderator);
            await SetAccess(Moderator, "tv", "auto");
            await RequestAnime(Alice, "s");
            await AskLanguage(Bob, "s");   // Bob follows Alice's sub request

            await AskLanguage(Moderator, "d");

            Latest(Alice).Content.Should().Contain("Now set to **Dub**");
            Latest(Bob).Content.Should().StartWith($"<@{Bob}>").And.Contain("Now set to **Dub**").And.Contain("You're following alice's request");
            Latest(Moderator).Content.Should().NotContain("Now set to **Dub** after a later request");
        }
    }
}
