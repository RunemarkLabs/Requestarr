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
            Sonarr.Season(id, 1, aired: 3, withFiles: 3);

            var run = await AskLanguage(Bob, "d");

            Sonarr.SeriesTags[id].Should().BeEquivalentTo(new[] { 41, tags.Dub });
            run.LastReply.Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain("now set to **Dub**").And.Contain("episodes already downloaded stay as they are");
            RequestStore.All().Should().BeEmpty();
        }

        // Task 4 item 13: a series that was sent to Sonarr but has nothing downloaded is not "already
        // available"; the reply says where it really stands, and the switcher follows the request.
        [Test]
        public async Task switching_a_series_that_was_sent_but_not_downloaded_says_so()
        {
            var tags = await SetUp();
            await SetAccess(Alice, "tv", "auto");
            await RequestAnime(Alice, "s");
            var id = Sonarr.SeriesTags.Keys.Single();

            var run = await AskLanguage(Bob, "d");

            run.LastReply.Content.Should().StartWith(DiscordText.FinalSearching).And.Contain("was sent to Sonarr and nothing is downloaded yet").And.Contain("now set to **Dub**")
                .And.NotContain(DiscordText.FinalAvailable).And.NotContain("in the library").And.NotContain(DiscordText.LanguageLine(AnimeLanguage.Sub));
            Sonarr.SeriesTags[id].Should().Equal(tags.Dub);
            RequestFor().Language.Should().Be(AnimeLanguage.Dub);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}>").And.Contain(DiscordText.LanguageLine(AnimeLanguage.Dub)).And.Contain("Now set to **Dub**");
            Subscribers.ForRequest(RequestFor().Id).Select(s => s.UserId).Should().Equal(new[] { UserFor(Bob).Id }, "it's still on its way: Bob follows it");
        }

        [TestCase(Requestarr.Core.Requests.DownloadProgress.Downloading, false, "is downloading", "⬇️ **Downloading**")]
        [TestCase(Requestarr.Core.Requests.DownloadProgress.None, true, "is partly in the library", "🎉 **Partly available**")]
        public async Task the_switch_reply_matches_a_download_in_progress(Requestarr.Core.Requests.DownloadProgress progress, bool partly, string says, string header)
        {
            await SetUp();
            await SetAccess(Alice, "tv", "auto");
            await RequestAnime(Alice, "s");
            var request = RequestFor();
            request.Progress = progress;
            request.AvailableSeasons = partly ? new System.Collections.Generic.List<int> { 1 } : new System.Collections.Generic.List<int>();
            RequestStore.Update(request);

            var run = await AskLanguage(Bob, "d");

            run.LastReply.Content.Should().StartWith(header).And.Contain(says).And.Contain("now set to **Dub**");
        }

        [Test]
        public async Task a_library_series_with_every_aired_episode_downloaded_is_already_available()
        {
            var tags = await SetUp();
            await SetAccess(Alice, "tv", "auto");
            await RequestAnime(Alice, "s");
            var request = RequestFor();
            request.Status = Requestarr.Core.Requests.RequestStatus.Available;
            RequestStore.Update(request);

            var run = await AskLanguage(Bob, "d");

            run.LastReply.Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain("Good news").And.Contain("now set to **Dub**");
            Sonarr.SeriesTags.Values.Single().Should().Equal(tags.Dub);
        }

        [Test]
        public async Task in_the_library_the_losing_language_changes_nothing()
        {
            var tags = await SetUp();
            var id = Sonarr.AddToLibrary(OnePiece, "anime", tags.Dub);
            Sonarr.Season(id, 1, aired: 3, withFiles: 3);

            var run = await AskLanguage(Bob, "s");

            Sonarr.SeriesTags[id].Should().Equal(tags.Dub);
            run.LastReply.Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain("set to **Dub** (dub wins here)");

            Sonarr.Season(id, 1, aired: 3, withFiles: 0);
            (await AskLanguage(Bob, "s")).LastReply.Content.Should().StartWith(DiscordText.FinalSearching).And.Contain("nothing is downloaded yet, set to **Dub**");
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
