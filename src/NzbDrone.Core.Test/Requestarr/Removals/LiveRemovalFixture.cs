using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Categories;
using Requestarr.Core.Admin;
using Requestarr.Core.Anime;
using Requestarr.Core.Announcements;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Metadata;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Removals
{
    // Live (task 4): the real bot in the Discord test server with the dev stack's TEST Radarr and Sonarr.
    // A movie removed by its requester's vote with no keeps (read back: gone from Radarr with its file,
    // not excluded); a TV season removed by the admin's Force (its files gone, the season unmonitored,
    // the show still there); a title nobody requested, from the panel's Remove a title, kept by one keep.
    // The vote window is passed by moving the removal service's clock forward 7 days, not by waiting.
    // Uses only the test accounts in DISCORD_TEST_MEMBERS. Never runs in CI (ManualTest). Needs the
    // stack's Requestarr stopped: docker/dev.sh reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveRemovalFixture : DbTest
    {
        private const int Matrix = 603;
        private const int Heat = 949;
        private const int Severance = 371980;

        private static string RadarrUrl => LiveDev.RadarrUrl;
        private static string RadarrKey => LiveDev.RadarrKey;
        private static string SonarrUrl => LiveDev.SonarrUrl;
        private static string SonarrKey => LiveDev.SonarrKey;

        [Test]
        public async Task removals_by_vote_force_and_keep_in_the_test_server()
        {
            var token = LiveDev.Token;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(RadarrKey) || string.IsNullOrWhiteSpace(SonarrKey))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN / RADARR_TEST_API_KEY / SONARR_TEST_API_KEY not set");
            }

            UseRealHttp();
            var settings = new DiscordSettingsStore();
            settings.Set(LiveDev.Settings());

            Mocker.SetConstant<IDiscordSettingsStore>(settings);
            Mocker.SetConstant<IRequestarrUserRepository>(Mocker.Resolve<RequestarrUserRepository>());
            Mocker.SetConstant<IUserIdentityRepository>(Mocker.Resolve<UserIdentityRepository>());
            Mocker.SetConstant<IUserMediaAccessRepository>(Mocker.Resolve<UserMediaAccessRepository>());
            Mocker.SetConstant<IMediaServerAccountRepository>(Mocker.Resolve<MediaServerAccountRepository>());
            Mocker.SetConstant<IMediaServerAccessRequestRepository>(Mocker.Resolve<MediaServerAccessRequestRepository>());
            Mocker.SetConstant<IAuditRepository>(Mocker.Resolve<AuditRepository>());
            Mocker.SetConstant<IDiscordGuildConfigRepository>(Mocker.Resolve<DiscordGuildConfigRepository>());
            Mocker.SetConstant<IAuditService>(Mocker.Resolve<AuditService>());
            Mocker.SetConstant<IBackendRepository>(Mocker.Resolve<BackendRepository>());
            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.SetConstant<IRequestSubscriberRepository>(Mocker.Resolve<RequestSubscriberRepository>());
            Mocker.SetConstant<IRemovalProposalRepository>(Mocker.Resolve<RemovalProposalRepository>());
            Mocker.SetConstant<IRemovalFlagRepository>(Mocker.Resolve<RemovalFlagRepository>());
            Mocker.SetConstant<IRemovalPromptRepository>(Mocker.Resolve<RemovalPromptRepository>());
            Mocker.SetConstant<NzbDrone.Core.Configuration.IConfigRepository>(Mocker.Resolve<NzbDrone.Core.Configuration.ConfigRepository>());
            Mocker.SetConstant<NzbDrone.Core.Configuration.IConfigService>(Mocker.Resolve<NzbDrone.Core.Configuration.ConfigService>());
            Mocker.SetConstant<IRemovalSettings>(Mocker.Resolve<RemovalSettings>());
            Mocker.SetConstant<IRadarrApi>(Mocker.Resolve<RadarrApi>());
            Mocker.SetConstant<ISonarrApi>(Mocker.Resolve<SonarrApi>());
            Mocker.SetConstant<IBackendService>(Mocker.Resolve<BackendService>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr (test)", Url = RadarrUrl, ApiKey = RadarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true } },
                Sonarr = new List<BackendInstanceProvisioning> { new() { Name = "Sonarr (test)", Url = SonarrUrl, ApiKey = SonarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/tv", SeriesType = "standard", Default = true } }
            });
            Mocker.Resolve<IBackendService>().CheckAllAsync().GetAwaiter().GetResult();
            var tmdb = new TmdbSettingsStore();
            tmdb.Set(Environment.GetEnvironmentVariable("TMDB_API_KEY"));
            Mocker.SetConstant<ITmdbSettingsStore>(tmdb);
            Mocker.SetConstant<ITmdbClient>(new TmdbClient(tmdb, LogManager.GetLogger("LiveTmdb")));
            Mocker.SetConstant<IMetadataService>(Mocker.Resolve<MetadataService>());
            Mocker.SetConstant<IAvailabilitySettingsStore>(new AvailabilitySettingsStore());

            IGuildSetupService guildSetup = null;
            IUserManagementService users = null;
            IPanelService panels = null;
            IRequestFeed feed = null;

            // Task 4e: no media server is connected (as on a real server today); the outage record and the access
            // request stores are real.
            Mocker.SetConstant<Requestarr.Core.MediaServers.IMediaServerConnections>(new Requestarr.Core.MediaServers.MediaServerConnections());
            Mocker.SetConstant<Requestarr.Core.Backends.IOutageRepository>(Mocker.Resolve<Requestarr.Core.Backends.OutageRepository>());
            Mocker.SetConstant<Requestarr.Core.Access.IAccessRequestRepository>(Mocker.Resolve<Requestarr.Core.Access.AccessRequestRepository>());
            Mocker.SetConstant<Requestarr.Core.Access.IWelcomeGuardRepository>(Mocker.Resolve<Requestarr.Core.Access.WelcomeGuardRepository>());
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => users),
                new Lazy<IPanelService>(() => panels),
                new Lazy<IRequestFeed>(() => feed),
                new Lazy<Requestarr.Core.Access.IAccessRequestService>(() => null),
                Mocker.Resolve<IEventAggregator>(),
                LogManager.GetLogger("LiveDiscord"));

            var gateway = new DiscordNetGateway(host, settings);
            Mocker.SetConstant<IDiscordClientHost>(host);
            Mocker.SetConstant<IDiscordGuildGateway>(gateway);
            guildSetup = Mocker.Resolve<GuildSetupService>();
            Mocker.SetConstant(guildSetup);
            feed = Mocker.Resolve<RequestFeed>();
            Mocker.SetConstant(feed);
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            Mocker.SetConstant<IAdminQueue>(new AdminQueue(new IAdminQueueSource[]
            {
                Mocker.Resolve<PendingRequestsSource>(),
                Mocker.Resolve<TroubleSource>(),
                Mocker.Resolve<PlexLinkSource>(),
                Mocker.Resolve<DownInstanceSource>(),
                Mocker.Resolve<RemovalSource>()
            }));
            Mocker.SetConstant<IAnnouncementService>(Mocker.Resolve<AnnouncementService>());
            Mocker.SetConstant<IConnectionMonitor>(Mocker.Resolve<ConnectionMonitor>());
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            Mocker.SetConstant<IRemovalService>(Mocker.Resolve<RemovalService>());
            var handlers = new IInteractionHandler[] { Mocker.Resolve<AdminConsoleHandler>(), Mocker.Resolve<RequestHandler>(), Mocker.Resolve<ApprovalHandler>(), Mocker.Resolve<SeasonsHandler>(), Mocker.Resolve<MediaServerHandler>(), Mocker.Resolve<PanelHandler>(), Mocker.Resolve<AdminRequestsHandler>(), Mocker.Resolve<OutageHandler>(), Mocker.Resolve<RemovalHandler>() };
            var dispatcher = new InteractionDispatcher(handlers, LogManager.GetLogger("LiveDiscord"));

            var removals = Mocker.Resolve<IRemovalService>() as RemovalService;
            var availability = Mocker.Resolve<AvailabilityService>();
            var http = Mocker.Resolve<IHttpClient>();

            try
            {
                await host.StartAsync(settings.Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

                var guild = host.Client.GetGuild(settings.Current.GuildId);
                var config = guildSetup.GetConfig();
                var adminChannel = ulong.Parse(config.AdminChannelId);
                var feedChannel = ulong.Parse(config.FeedChannelId);
                var feedRest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(feedChannel);
                FakeInteraction AsOwner(FakeInteraction i) => i.By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel);
                FakeInteraction InFeed(FakeInteraction i) => i.By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(feedChannel);
                var requests = Mocker.Resolve<IRequestRepository>();

                // The only test account: requests as it, and it votes. Everyone else in the server is left alone.
                var member = LiveTestMembers.Pick(await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync())[0];
                FakeInteraction AsMember(FakeInteraction i, ulong channel) => i.By(member.Id, member.Username).InGuild(guild.Id).InChannel(channel);
                await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Add}:{member.Id}")));
                await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Access}:{member.Id}:movie", "auto")));
                await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Access}:{member.Id}:tv", "auto")));
                var channel = ulong.Parse(users.FindByDiscordId(member.Id).PrivateChannelId);
                var rest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(channel);
                Log($"[setup] {member.Username} added with Movies and TV auto-approve; vote window {Mocker.Resolve<IRemovalSettings>().VoteDays} days");

                async Task<string> LatestIn(IMessageChannel c) => (await c.GetMessagesAsync(1).FlattenAsync()).Single().Content.Replace("\n", " | ");
                async Task<IMessage> Feed(Request r) => await feedRest.GetMessageAsync(ulong.Parse(requests.Get(r.Id).FeedMessageId));
                string RemovalLine(IMessage card) => card.Embeds.First().Description.Split('\n').SingleOrDefault(l => l.StartsWith("🗑️ Remov"));

                // The vote window, passed without waiting a week: the removal service's clock moves forward.
                async Task CloseVote()
                {
                    var later = DateTime.UtcNow.AddDays(7).AddMinutes(1);
                    removals.Clock = () => later;
                    await removals.TickAsync();
                    removals.Clock = () => DateTime.UtcNow;
                }

                // 1. A movie removed by its requester's vote, with no keeps.
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, member.Id, $"req-make:movie:{Matrix}", $"🎬 **Movies request** · details (tmdb:{Matrix})");
                var movie = requests.All().Single(r => r.TmdbId == Matrix);
                var moviePath = (string)(await Get(http, RadarrUrl, RadarrKey, $"movie/{movie.ExternalItemId}"))["path"];
                var movieFile = $"{moviePath}/The.Matrix.1999.1080p.BluRay.x264-TEST.mkv";
                Docker("requestarr-dev-radarr-test-1", $"mkdir -p '{moviePath}' && truncate -s 1500M '{movieFile}'");
                await Post(http, RadarrUrl, RadarrKey, "command", new JObject { ["name"] = "RescanMovie", ["movieId"] = movie.ExternalItemId });
                await Until(async () => (bool)(await Get(http, RadarrUrl, RadarrKey, $"movie/{movie.ExternalItemId}"))["hasFile"]);
                await availability.CheckAllAsync();
                movie = requests.Get(movie.Id);
                Log($"[movie] request #{movie.Id} {movie.Label} status={movie.Status}; Radarr movie {movie.ExternalItemId} has its file ({movieFile})");
                movie.Status.Should().Be(RequestStatus.Available);

                var userCard = await Send(dispatcher, AsMember(FakeInteraction.Button($"{RemovalRoutes.User}:{movie.Id}"), channel));
                Log($"[movie] {DiscordText.RemoveRequestLabel} on the request message: \"{userCard.LastReply.Content}\" buttons=[{string.Join("] [", userCard.LastReply.Buttons.Select(b => b.Label))}]");
                var started = await Send(dispatcher, AsMember(FakeInteraction.Button(userCard.LastReply.Buttons.Single().CustomId), channel));
                Log($"[movie] started: {started.LastReply.Content}");
                Log($"[movie] {member.Username}'s channel, read back: \"{await LatestIn(rest)}\"");
                Log($"[movie] feed card, read back: \"{RemovalLine(await Feed(movie))}\" buttons=[{string.Join("] [", Buttons(await Feed(movie)))}]");
                var during = (bool)(await Get(http, RadarrUrl, RadarrKey, $"movie/{movie.ExternalItemId}"))["monitored"];
                Log($"[movie] during the vote Radarr still has it, monitored={during}, file there={Exists("requestarr-dev-radarr-test-1", movieFile)}");

                await CloseVote();
                await Until(() => Task.FromResult(!Exists("requestarr-dev-radarr-test-1", movieFile)));

                var stillThere = (JArray)await Get(http, RadarrUrl, RadarrKey, $"movie?tmdbId={Matrix}");
                var excluded = ((JArray)await Get(http, RadarrUrl, RadarrKey, "exclusions")).Count(e => (int?)e["tmdbId"] == Matrix);
                Log($"[movie] after the vote closed with no keeps: Radarr movies with tmdbId {Matrix}: {stillThere.Count}; file there={Exists("requestarr-dev-radarr-test-1", movieFile)}; folder there={Exists("requestarr-dev-radarr-test-1", moviePath)}; on Radarr's exclusion list: {excluded}");
                Log($"[movie] request #{movie.Id} status={requests.Get(movie.Id).Status}; {member.Username}'s request message, read back: \"{await LatestIn(rest)}\"");
                Log($"[movie] feed card, read back: \"{RemovalLine(await Feed(movie))}\"");
                stillThere.Should().BeEmpty();
                Exists("requestarr-dev-radarr-test-1", movieFile).Should().BeFalse();
                excluded.Should().Be(0);

                // 2. A TV season removed by the admin's Force; the show stays.
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, member.Id, $"req-tv-ok:{Severance}:1-2", $"📺 **TV request** · details (tvdb:{Severance})");
                var show = requests.All().Single(r => r.TvdbId == Severance);
                var seriesId = show.ExternalItemId.Value;
                var seriesPath = (string)(await Get(http, SonarrUrl, SonarrKey, $"series/{seriesId}"))["path"];
                var aired = ((JArray)await Get(http, SonarrUrl, SonarrKey, $"episode?seriesId={seriesId}")).Children<JObject>()
                    .Where(e => (int)e["seasonNumber"] is 1 or 2 && e["airDateUtc"] != null && (DateTime)e["airDateUtc"] <= DateTime.UtcNow)
                    .Select(e => ((int)e["seasonNumber"], (int)e["episodeNumber"])).ToList();
                Docker("requestarr-dev-sonarr-test-1", $"mkdir -p '{seriesPath}/Season 01' '{seriesPath}/Season 02' && " +
                    string.Join(" && ", aired.Select(e => $"truncate -s 900M '{seriesPath}/Season {e.Item1:00}/Severance.S{e.Item1:00}E{e.Item2:00}.1080p.WEB.h264-TEST.mkv'")));
                await Post(http, SonarrUrl, SonarrKey, "command", new JObject { ["name"] = "RescanSeries", ["seriesId"] = seriesId });
                await Until(async () => ((JArray)await Get(http, SonarrUrl, SonarrKey, $"episodefile?seriesId={seriesId}")).Count >= aired.Count);
                await availability.CheckAllAsync();
                show = requests.Get(show.Id);
                int Files(JArray files, int season) => files.Count(f => (int)f["seasonNumber"] == season);
                var before = (JArray)await Get(http, SonarrUrl, SonarrKey, $"episodefile?seriesId={seriesId}");
                Log($"[tv] request #{show.Id} {show.Label} seasons 1-2 status={show.Status}; Sonarr episode files: season 1 {Files(before, 1)}, season 2 {Files(before, 2)}");

                var adminCard = await Send(dispatcher, InFeed(FakeInteraction.Button($"{RemovalRoutes.Feed}:{show.Id}")).OnMessage(ulong.Parse(show.FeedMessageId)));
                Log($"[tv] Delete on the feed card: \"{adminCard.LastReply.Content.Replace("\n", " | ")}\" menu=[{string.Join(", ", adminCard.LastReply.SelectMenu.Options.Select(o => o.Label))}]");
                var scoped = await Send(dispatcher, InFeed(FakeInteraction.SelectMenu(adminCard.LastReply.SelectMenu.CustomId, "1")));
                var confirm = await Send(dispatcher, InFeed(FakeInteraction.Button(scoped.LastReply.Buttons.Single(b => b.Label == DiscordText.ForceNowLabel).CustomId)));
                Log($"[tv] Force: \"{confirm.LastReply.Content}\"");
                var forced = await Send(dispatcher, InFeed(FakeInteraction.Button(confirm.LastReply.Buttons.Single(b => b.Label == DiscordText.ConfirmDeleteLabel).CustomId)));
                Log($"[tv] {forced.LastReply.Content}");
                await Until(async () => Files((JArray)await Get(http, SonarrUrl, SonarrKey, $"episodefile?seriesId={seriesId}"), 1) == 0);

                var after = (JArray)await Get(http, SonarrUrl, SonarrKey, $"episodefile?seriesId={seriesId}");
                var series = (JObject)await Get(http, SonarrUrl, SonarrKey, $"series/{seriesId}");
                var seasons = series["seasons"].Where(s => (int)s["seasonNumber"] is 1 or 2).Select(s => $"season {(int)s["seasonNumber"]} monitored={(bool)s["monitored"]}");
                var onDisk = Run("exec", "requestarr-dev-sonarr-test-1", "sh", "-c", $"ls '{seriesPath}/Season 01' 2>/dev/null | wc -l; ls '{seriesPath}/Season 02' | wc -l").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Log($"[tv] after Force on season 1: series {seriesId} still in Sonarr: {(string)series["title"]}; {string.Join(", ", seasons)}; episode files: season 1 {Files(after, 1)}, season 2 {Files(after, 2)}; files on disk: Season 01 {onDisk[0].Trim()}, Season 02 {onDisk[1].Trim()}");
                Log($"[tv] request #{show.Id} seasons now {string.Join(", ", requests.Get(show.Id).Seasons)}; {member.Username}'s request message, read back: \"{await LatestIn(rest)}\"");
                Log($"[tv] feed card, read back: \"{RemovalLine(await Feed(show))}\"");
                Files(after, 1).Should().Be(0);
                Files(after, 2).Should().Be(Files(before, 2));
                series["seasons"].Single(s => (int)s["seasonNumber"] == 1)["monitored"].Value<bool>().Should().BeFalse();

                // 3. A title nobody requested here, from the panel's Remove a title; one keep cancels it.
                var heat = (JObject)await Get(http, RadarrUrl, RadarrKey, $"movie/lookup/tmdb?tmdbId={Heat}");
                heat["qualityProfileId"] = (int)((JArray)await Get(http, RadarrUrl, RadarrKey, "qualityprofile")).First(p => (string)p["name"] == "HD-1080p")["id"];
                heat["rootFolderPath"] = "/data/movies";
                heat["monitored"] = true;
                heat["addOptions"] = new JObject { ["searchForMovie"] = false };
                await Post(http, RadarrUrl, RadarrKey, "movie", heat);
                var heatMovie = (JObject)((JArray)await Get(http, RadarrUrl, RadarrKey, $"movie?tmdbId={Heat}")).Single();
                var heatPath = (string)heatMovie["path"];
                Docker("requestarr-dev-radarr-test-1", $"mkdir -p '{heatPath}' && truncate -s 1400M '{heatPath}/Heat.1995.1080p.BluRay.x264-TEST.mkv'");
                await Post(http, RadarrUrl, RadarrKey, "command", new JObject { ["name"] = "RescanMovie", ["movieId"] = (int)heatMovie["id"] });
                await Until(async () => (bool)(await Get(http, RadarrUrl, RadarrKey, $"movie/{(int)heatMovie["id"]}"))["hasFile"]);
                Log($"[ownerless] Heat (1995) added to the test Radarr directly (not through Requestarr), with a file");

                var form = await Send(dispatcher, AsOwner(FakeInteraction.Button(RemovalRoutes.Search)));
                var results = await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId).WithField(RemovalRoutes.SearchField, "heat")));
                Log($"[ownerless] Remove a title, \"heat\": {results.LastReply.Content} [{string.Join("] [", results.LastReply.SelectMenu.Options.Select(o => o.Label))}]");
                var pick = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu(RemovalRoutes.Pick, results.LastReply.SelectMenu.Options.First(o => o.Label.StartsWith("Heat (1995)")).Value)));
                Log($"[ownerless] its card: \"{pick.LastReply.Content.Replace("\n", " | ")}\"");
                var go = await Send(dispatcher, AsOwner(FakeInteraction.Button(pick.LastReply.Buttons.Single(b => b.Label == DiscordText.StartRemovalLabel).CustomId)));
                Log($"[ownerless] {go.LastReply.Content}");

                var proposal = Mocker.Resolve<IRemovalProposalRepository>().All().Single(p => p.TmdbId == Heat);
                var prompt = await rest.GetMessageAsync(ulong.Parse(Mocker.Resolve<IRemovalPromptRepository>().ForProposal(proposal.Id).Single().MessageId));
                Log($"[ownerless] {member.Username}'s prompt, read back: \"{prompt.Content.Replace("\n", " | ")}\" buttons=[{string.Join("] [", Buttons(prompt))}] card=\"{prompt.Embeds.FirstOrDefault()?.Title}\"");
                var ownerlessCard = await feedRest.GetMessageAsync(ulong.Parse(Mocker.Resolve<IRemovalProposalRepository>().Get(proposal.Id).FeedMessageId));
                Log($"[ownerless] its own feed card, read back: \"{ownerlessCard.Embeds.First().Title}\" \"{ownerlessCard.Embeds.First().Description.Replace("\n", " | ")}\" buttons=[{string.Join("] [", Buttons(ownerlessCard))}]");

                var kept = await Send(dispatcher, AsMember(FakeInteraction.Button($"{RemovalRoutes.Keep}:{proposal.Id}").OnMessage(prompt.Id), channel));
                Log($"[ownerless] {DiscordText.RemovalKeepLabel}: {kept.LastReply.Content}");
                await CloseVote();

                var heatAfter = (JObject)await Get(http, RadarrUrl, RadarrKey, $"movie/{(int)heatMovie["id"]}");
                var outcome = await rest.GetMessageAsync(ulong.Parse(Mocker.Resolve<IRemovalPromptRepository>().ForProposal(proposal.Id).Single().MessageId));
                Log($"[ownerless] vote closed with 1 keep: removal {Mocker.Resolve<IRemovalProposalRepository>().Get(proposal.Id).Status}; Heat still in Radarr: {(string)heatAfter["title"]}, hasFile={(bool)heatAfter["hasFile"]}, monitored={(bool)heatAfter["monitored"]}");
                Log($"[ownerless] {member.Username}'s prompt reposted, read back: \"{outcome.Content.Replace("\n", " | ")}\" buttons=[{string.Join("] [", Buttons(outcome))}]");
                ownerlessCard = await feedRest.GetMessageAsync(ulong.Parse(Mocker.Resolve<IRemovalProposalRepository>().Get(proposal.Id).FeedMessageId));
                Log($"[ownerless] feed card, read back: \"{ownerlessCard.Embeds.First().Description.Replace("\n", " | ")}\"");
                ((bool)heatAfter["hasFile"]).Should().BeTrue();
                outcome.Content.Should().Contain("**Kept**");

                // Images travel with the message: the feed card's spacer was uploaded with it, so Discord serves
                // it from the message's own attachments (no GitHub link anywhere).
                var image = new Uri((await Feed(show)).Embeds.First().Image.Value.Url);
                Log($"[assets] feed card embed image, read back: {image.Host}{image.AbsolutePath}");
                (image.Host is "cdn.discordapp.com" or "media.discordapp.net").Should().BeTrue();
                image.AbsolutePath.Should().StartWith("/attachments/").And.EndWith("/" + DiscordAssets.FeedCardWidth);
            }
            finally
            {
                if (host.Client?.GetGuild(settings.Current.GuildId) is { } cleanupGuild)
                {
                    await cleanupGuild.BulkOverwriteApplicationCommandAsync(Array.Empty<ApplicationCommandProperties>());
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.sh reset-dev to restore the dev stack)");
            }
        }

        private static async Task Until(Func<Task<bool>> done)
        {
            for (var i = 0; i < 40 && !await done(); i++)
            {
                await Task.Delay(2000);
            }
        }

        private static bool Exists(string container, string path)
        {
            var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { "exec", container, "test", "-e", path })
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            process.WaitForExit(30000);
            return process.ExitCode == 0;
        }

        // As the container's app user (1000), like files Sonarr and Radarr import themselves, so they can delete them.
        private static void Docker(string container, string script) => Run("exec", "-u", "1000:1000", container, "sh", "-c", script);

        private static string Run(params string[] args)
        {
            var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(60000);
            process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd());
            return output;
        }

        private static async Task<JToken> Get(IHttpClient http, string url, string key, string resource)
        {
            var request = new HttpRequestBuilder($"{url}/api/v3/{resource}").SetHeader("X-Api-Key", key).Build();
            return JToken.Parse((await http.GetAsync(request)).Content);
        }

        private static async Task Post(IHttpClient http, string url, string key, string resource, JObject body)
        {
            var request = new HttpRequestBuilder($"{url}/api/v3/{resource}").SetHeader("X-Api-Key", key).Post().Build();
            request.Headers.ContentType = "application/json";
            request.SetContent(body.ToString());
            await http.ExecuteAsync(request);
        }

        private static List<string> Buttons(IMessage message) =>
            message.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => b.Label).ToList();

        // Posts the request's message as the bot (as /request leaves it), then presses its button on it.
        private static async Task RequestInRealMessage(InteractionDispatcher dispatcher, IDiscordGuildGateway gateway, ulong guild, ulong channel, ulong user, string customId, string text)
        {
            var message = await gateway.SendMessageAsync(channel, new InteractionReply { Content = text });
            var responder = new RecordingResponder(null, channel, message, false);
            await dispatcher.DispatchAsync(FakeInteraction.Button(customId).By(user).InGuild(guild).InChannel(channel).OnMessage(message).Build(), responder);
            await gateway.EditMessageAsync(channel, message, responder.LastReply);
            Log($"[request] {customId} -> {responder.LastReply.Content.Replace("\n", " | ")}");
        }

        private static async Task<InteractionRun> Send(InteractionDispatcher dispatcher, FakeInteraction interaction)
        {
            var built = interaction.Build();
            var responder = new RecordingResponder(null, built.ChannelId, built.MessageId, false);
            var outcome = await dispatcher.DispatchAsync(built, responder);
            return new InteractionRun(outcome, responder);
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
