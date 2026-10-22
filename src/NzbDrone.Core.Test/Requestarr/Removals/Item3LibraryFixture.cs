using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Removals
{
    // Task 4 items 2, 7, 8 and 12: Remove a title searches every library (titles nobody requested go
    // straight to the vote); season removals; one open removal per title and season scope; deleting
    // never excludes; the settings.
    [TestFixture]
    public class Item3LibraryFixture : RemovalScenario
    {
        private Task<InteractionRun> Search(string term) =>
            Harness.SendAsync(AsAdmin(FakeInteraction.ModalSubmit(RemovalRoutes.SearchForm).WithField(RemovalRoutes.SearchField, term)));

        [Test]
        public async Task remove_a_title_searches_every_library_and_an_ownerless_title_goes_straight_to_the_vote()
        {
            var form = await AdminButton(RemovalRoutes.Search);
            form.Responder.LastModal.CustomId.Should().Be(RemovalRoutes.SearchForm);

            var results = (await Search("arr")).LastReply;
            results.Ephemeral.Should().BeTrue();
            results.SelectMenu.Options.Select(o => o.Label).Should().Contain("Arrival (2016) · Radarr · not requested");
            (await Search("nothing like this")).LastReply.Content.Should().Be(DiscordText.RemovalNothingFound);

            var arrival = results.SelectMenu.Options.First(o => o.Label.StartsWith("Arrival (2016) · Radarr ·")).Value;
            var card = (await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu(RemovalRoutes.Pick, arrival)))).LastReply;
            card.Content.Should().Contain("Not requested through Requestarr").And.Contain("straight to the vote");
            var started = (await Press(card, DiscordText.StartRemovalLabel)).LastReply;

            started.Content.Should().StartWith("Started:");
            Proposal().Status.Should().Be(RemovalStatus.Voting);
            Proposal().RequestId.Should().BeNull();
            PromptOf(Alice).Content.Should().Contain("**Arrival (2016)**");
            PromptOf(Bob).Should().NotBeNull("everyone with Movies access is asked, nobody first");

            var feedCard = Feed.Messages.Last(m => m.Embeds.FirstOrDefault()?.Footer == "Removal #1");
            feedCard.Embeds[0].Title.Should().Be("Arrival (2016)");
            feedCard.Embeds[0].Description.Should().StartWith("🗑️ Removal vote until").And.Contain("not requested through Requestarr");
            feedCard.Embeds[0].ImageUrl.Should().Be(DiscordText.FeedCardWidthUrl);
            feedCard.Buttons.Select(b => b.CustomId).Should().Equal("rm-force:1", "rm-drop:1");

            await Tick(TimeSpan.FromDays(7));

            Radarr.Deletes.Select(d => d.Id).Should().Equal(7);
            PromptOf(Alice).Content.Should().Contain("🗑️ **Removed**");
            Feed.Messages.Last(m => m.Embeds.FirstOrDefault()?.Footer == "Removal #1").Embeds[0].Description.Should().StartWith("🗑️ Removed by vote");
            Feed.Messages.Count(m => m.Embeds.FirstOrDefault()?.Footer == "Removal #1").Should().Be(1, "reposted at every step, never piling up");
        }

        [Test]
        public async Task a_title_whose_requester_was_removed_is_ownerless()
        {
            var request = await DownloadedMovie();
            await Harness.SendAsync(AdminCommand("user remove", Alice));

            var card = (await FeedClick(request.Id, $"{RemovalRoutes.Feed}:{request.Id}")).LastReply;
            card.Content.Should().Contain("Not requested through Requestarr (or its requester was removed)");
            await Press(card, DiscordText.StartRemovalLabel);

            Proposal().Status.Should().Be(RemovalStatus.Voting);
            Proposal().RequesterUserId.Should().BeNull();
            PromptOf(Bob).Should().NotBeNull();
        }

        [Test]
        public async Task removing_a_season_deletes_its_files_and_unmonitors_it_and_the_show_stays()
        {
            var request = await DownloadedShow(Alice, 1, 2);
            var series = request.ExternalItemId.Value;

            var card = (await UserRemove(Alice, request.Id)).LastReply;
            card.SelectMenu.Options.Select(o => o.Value).Should().Equal("all", "1", "2");
            card = (await Choose(Alice, card.SelectMenu.CustomId, "1")).LastReply;
            card.Content.Should().Contain("Remove **Severance (2022)**, season 1?");
            await Click(Alice, card.Buttons.Single().CustomId);

            PromptOf(Bob).Content.Should().Contain("**Severance (2022)**, season 1 may be removed");
            await Tick(TimeSpan.FromDays(7));

            Sonarr.SeasonDeletes.Should().ContainSingle();
            (Sonarr.SeasonDeletes[0].SeriesId, Sonarr.SeasonDeletes[0].Seasons.Single()).Should().Be((series, 1));
            Sonarr.Deletes.Should().BeEmpty("the show stays");
            Sonarr.Episodes[series].Where(e => e.SeasonNumber == 1).Should().OnlyContain(e => !e.HasFile && !e.Monitored);
            Sonarr.Episodes[series].Where(e => e.SeasonNumber == 2).Should().OnlyContain(e => e.HasFile);

            var after = Reload(request.Id);
            after.Status.Should().NotBe(RequestStatus.Removed);
            after.Seasons.Should().Equal(2);
            Latest(Alice).Content.Should().Contain("🗑️ Season 1 removed after a vote.");
            PromptOf(Bob).Content.Should().Contain("**Severance (2022)**, season 1 was removed").And.Contain("The rest of the show stays.");
        }

        [Test]
        public async Task the_admin_forces_a_season_removal_and_the_whole_show_closes_the_request()
        {
            var request = await DownloadedShow(Alice, 1, 2);

            await AdminStart(request, scope: "2", force: true);
            Sonarr.SeasonDeletes.Should().ContainSingle().Which.Seasons.Should().Equal(2);
            Reload(request.Id).Seasons.Should().Equal(1);

            await AdminStart(request, scope: "all", force: true);
            Sonarr.Deletes.Should().ContainSingle();
            Reload(request.Id).Status.Should().Be(RequestStatus.Removed);
        }

        [Test]
        public async Task one_open_removal_per_title_and_seasons_overlapping_scopes_are_refused()
        {
            var request = await DownloadedShow(Alice, 1, 2);

            (await UserStartVote(Alice, request.Id, "1")).LastReply.Content.Should().StartWith("Started:");
            (await UserStartVote(Alice, request.Id, "1")).LastReply.Content.Should().StartWith("There's already a removal open for **Severance (2022)**, season 1 (voting)");
            (await UserStartVote(Alice, request.Id, "all")).LastReply.Content.Should().StartWith("There's already a removal open", "the whole show overlaps every season");
            (await UserStartVote(Alice, request.Id, "2")).LastReply.Content.Should().StartWith("Started:", "different seasons can run side by side");
            Proposals.All().Count(p => p.IsOpen).Should().Be(2);

            (await UserStartVote(Alice, request.Id, "7")).LastReply.Content.Should().Be(DiscordText.RemovalNoSuchSeason);
        }

        [Test]
        public void the_settings_default_to_seven_seven_and_day_five_and_provisioning_sets_them()
        {
            var settings = Mocker.Resolve<IRemovalSettings>();
            (settings.VoteDays, settings.RequesterTimeoutDays, settings.ReminderDay).Should().Be((7, 7, 5));

            settings.Apply(new RemovalProvisioning { VoteDays = 10, RequesterTimeoutDays = 4, ReminderDay = 3 });
            (settings.VoteDays, settings.RequesterTimeoutDays, settings.ReminderDay).Should().Be((10, 4, 3));
            Mocker.Resolve<NzbDrone.Core.Configuration.IConfigService>().RemovalVoteDays.Should().Be(10, "stored where the admin portal reads and writes settings");

            settings.Apply(new RemovalProvisioning { RequesterTimeoutDays = 2 });
            (settings.VoteDays, settings.ReminderDay).Should().Be((10, 0), "unnamed settings stay; a reminder on or after the timeout is no reminder");

            settings.Apply(null);
            settings.VoteDays.Should().Be(10);
        }

        [Test]
        public async Task radarr_and_sonarr_are_asked_to_delete_files_and_never_to_exclude()
        {
            var seen = new List<HttpRequest>();
            var http = new Mock<IHttpClient>();
            http.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequest>())).Callback<HttpRequest>(seen.Add)
                .ReturnsAsync((HttpRequest r) => new HttpResponse(r, new HttpHeader(), new System.Net.CookieCollection(), string.Empty, 0, HttpStatusCode.OK));
            var settings = new BackendSettings { Url = "http://arr.test", ApiKey = "k" };

            await new RadarrApi(http.Object).DeleteItemAsync(settings, 7);
            await new SonarrApi(http.Object).DeleteItemAsync(settings, 9);

            seen.Select(r => (r.Method.Method, r.Url.FullUri)).Should().Equal(
                ("DELETE", "http://arr.test/api/v3/movie/7?deleteFiles=true&addImportExclusion=false"),
                ("DELETE", "http://arr.test/api/v3/series/9?deleteFiles=true&addImportListExclusion=false"));

            // Seasons: those seasons' episode files deleted, then the seasons and their episodes unmonitored.
            seen.Clear();
            Respond<List<JObject>>(http, "episodefile", "[{\"id\":11,\"seasonNumber\":1},{\"id\":12,\"seasonNumber\":2},{\"id\":13,\"seasonNumber\":1}]");
            Respond<JObject>(http, "series/9", "{\"id\":9,\"seasons\":[{\"seasonNumber\":1,\"monitored\":true},{\"seasonNumber\":2,\"monitored\":true}]}");
            Respond<List<JObject>>(http, "episode", "[{\"id\":21,\"seasonNumber\":1},{\"id\":22,\"seasonNumber\":2}]");

            await new SonarrApi(http.Object).DeleteSeasonsAsync(settings, 9, new[] { 1 });

            seen.Select(r => (r.Method.Method, r.Url.FullUri.Replace("http://arr.test/api/v3/", string.Empty))).Should().Equal(("DELETE", "episodefile/bulk"), ("PUT", "series/9"), ("PUT", "episode/monitor"));
            JObject.Parse(seen[0].ContentSummary ?? System.Text.Encoding.UTF8.GetString(seen[0].ContentData))["episodeFileIds"].Select(t => (int)t).Should().Equal(11, 13);
            var series = JObject.Parse(System.Text.Encoding.UTF8.GetString(seen[1].ContentData));
            series["seasons"].Select(s => ((int)s["seasonNumber"], (bool)s["monitored"])).Should().Equal((1, false), (2, true));
            JObject.Parse(System.Text.Encoding.UTF8.GetString(seen[2].ContentData)).ToString(Newtonsoft.Json.Formatting.None).Should().Be("{\"episodeIds\":[21],\"monitored\":false}");
        }

        private static void Respond<T>(Mock<IHttpClient> http, string resource, string json)
            where T : new()
        {
            http.Setup(h => h.GetAsync<T>(It.Is<HttpRequest>(r => System.Text.RegularExpressions.Regex.IsMatch(r.Url.FullUri, $@"/api/v3/{resource}(\?|$)"))))
                .ReturnsAsync((HttpRequest r) => new HttpResponse<T>(new HttpResponse(r, new HttpHeader(), new System.Net.CookieCollection(), json, 0, HttpStatusCode.OK)));
        }
    }
}
