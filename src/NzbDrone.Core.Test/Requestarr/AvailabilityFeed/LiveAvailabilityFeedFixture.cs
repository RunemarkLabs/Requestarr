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
using Requestarr.Core.Anime;
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
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.AvailabilityFeed
{
    // Live: the real bot in the Discord test server with the dev stack's TEST Radarr and Sonarr.
    // A movie and a TV season are requested in real messages and approved from the real feed. The
    // test stack has no download client, so the files are placed in the test containers and the
    // instances rescan them (an import of existing files); the backup poll then finds them. The
    // feed and the reposted messages are read back from Discord. Never runs in CI (ManualTest).
    // Needs the stack's Requestarr stopped and a clean server: docker/dev.sh reset-uat --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveAvailabilityFeedFixture : DbTest
    {
        private const int Matrix = 603;
        private const int Severance = 371980;

        private static string RadarrUrl => $"http://localhost:{Environment.GetEnvironmentVariable("RADARR_TEST_PORT") ?? "17878"}";
        private static string SonarrUrl => $"http://localhost:{Environment.GetEnvironmentVariable("SONARR_TEST_PORT") ?? "18989"}";
        private static string RadarrKey => Environment.GetEnvironmentVariable("RADARR_TEST_API_KEY");
        private static string SonarrKey => Environment.GetEnvironmentVariable("SONARR_TEST_API_KEY");

        [Test]
        public async Task feed_cards_and_availability_read_back_from_the_test_server()
        {
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(RadarrKey) || string.IsNullOrWhiteSpace(SonarrKey))
            {
                Assert.Ignore("DISCORD_BOT_TOKEN / RADARR_TEST_API_KEY / SONARR_TEST_API_KEY not set");
            }

            UseRealHttp();
            var settings = new DiscordSettingsStore();
            settings.Set(new DiscordSettings
            {
                BotToken = token,
                ApplicationId = ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_APPLICATION_ID")),
                GuildId = ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_TEST_GUILD_ID")),
                AdminRoleId = ulong.TryParse(Environment.GetEnvironmentVariable("DISCORD_ADMIN_ROLE_ID"), out var adminRole) ? adminRole : null
            });

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
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => users),
                new Lazy<IPanelService>(() => panels),
                new Lazy<IRequestFeed>(() => feed),
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
            Mocker.SetConstant<Requestarr.Core.Admin.IAdminQueue>(new Requestarr.Core.Admin.AdminQueue(new Requestarr.Core.Admin.IAdminQueueSource[]
            {
                Mocker.Resolve<Requestarr.Core.Admin.PendingRequestsSource>(),
                Mocker.Resolve<Requestarr.Core.Admin.TroubleSource>(),
                Mocker.Resolve<Requestarr.Core.Admin.PlexLinkSource>(),
                Mocker.Resolve<Requestarr.Core.Admin.DownInstanceSource>()
            }));
            Mocker.SetConstant<IConnectionMonitor>(Mocker.Resolve<ConnectionMonitor>());
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            var availability = Mocker.Resolve<AvailabilityService>();
            var handlers = new IInteractionHandler[] { Mocker.Resolve<AdminConsoleHandler>(), Mocker.Resolve<RequestHandler>(), Mocker.Resolve<ApprovalHandler>(), Mocker.Resolve<SeasonsHandler>() };
            var dispatcher = new InteractionDispatcher(handlers, LogManager.GetLogger("LiveDiscord"));
            var http = Mocker.Resolve<IHttpClient>();

            try
            {
                await host.StartAsync(settings.Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

                var guild = host.Client.GetGuild(settings.Current.GuildId);
                var config = guildSetup.GetConfig();
                var adminChannel = ulong.Parse(config.AdminChannelId);
                var feedChannel = ulong.Parse(config.FeedChannelId);

                // The feed channel as Discord has it.
                var feedRest = (global::Discord.Rest.RestTextChannel)await host.Client.Rest.GetChannelAsync(feedChannel);
                var adminRest = (global::Discord.Rest.RestTextChannel)await host.Client.Rest.GetChannelAsync(adminChannel);
                Log($"[feed] #{feedRest.Name} topic=\"{feedRest.Topic}\" category={feedRest.CategoryId?.ToString() ?? "none"} (admin channel category {adminRest.CategoryId?.ToString() ?? "none"})");
                foreach (var o in feedRest.PermissionOverwrites)
                {
                    Log($"[feed]   overwrite {o.TargetType} {(o.TargetId == guild.Id ? "@everyone" : o.TargetId == guildSetup.AdminRoleId ? "admin role" : o.TargetId == host.Client.CurrentUser.Id ? "bot" : o.TargetId.ToString())}: view {(o.Permissions.ViewChannel == global::Discord.PermValue.Allow ? "allow" : o.Permissions.ViewChannel == global::Discord.PermValue.Deny ? "deny" : "inherit")}");
                }

                feedRest.Name.Should().Be(ChannelLayout.FeedChannelName);
                feedRest.PermissionOverwrites.Single(o => o.TargetId == guild.Id).Permissions.ViewChannel.Should().Be(global::Discord.PermValue.Deny);

                var members = await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync();
                var target = members.First(m => !m.IsBot && m.Id != guild.OwnerId);
                await Send(dispatcher, FakeInteraction.Button($"adm-add:{target.Id}").By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel));
                var channel = ulong.Parse(users.FindByDiscordId(target.Id).PrivateChannelId);
                Log($"[setup] {target.Username} added; request channel {channel}");

                var movieMessage = await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, target.Id, $"req-make:movie:{Matrix}", $"🎬 **Movies request** · details (tmdb:{Matrix})");
                var tvMessage = await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, target.Id, $"req-tv-ok:{Severance}:1", $"📺 **TV request** · details (tvdb:{Severance})");
                var requests = Mocker.Resolve<IRequestRepository>();

                await LogFeed(host, feedChannel, "after the two requests");

                // Approve both from their feed cards.
                foreach (var request in requests.All())
                {
                    var run = await Send(dispatcher, FakeInteraction.Button($"req-approve:{request.Id}").By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(feedChannel).OnMessage(ulong.Parse(request.FeedMessageId)));
                    Log($"[approve] request #{request.Id} from the feed; private reply: {run.LastReply.Content}");
                }

                await LogFeed(host, feedChannel, "after approving from the feed");

                // Files for the movie and every aired episode of season 1, placed in the test containers; then a rescan.
                var movie = requests.All().Single(r => r.TmdbId == Matrix);
                var series = requests.All().Single(r => r.TvdbId == Severance);
                var moviePath = (string)(await Get(http, RadarrUrl, RadarrKey, $"movie/{movie.ExternalItemId}"))["path"];
                var seriesPath = (string)(await Get(http, SonarrUrl, SonarrKey, $"series/{series.ExternalItemId}"))["path"];
                var aired = ((JArray)await Get(http, SonarrUrl, SonarrKey, $"episode?seriesId={series.ExternalItemId}")).Children<JObject>()
                    .Where(e => (int)e["seasonNumber"] == 1 && e["airDateUtc"] != null && (DateTime)e["airDateUtc"] <= DateTime.UtcNow).Select(e => (int)e["episodeNumber"]).ToList();

                Docker("requestarr-dev-radarr-test-1", $"mkdir -p '{moviePath}' && truncate -s 1500M '{moviePath}/The.Matrix.1999.1080p.BluRay.x264-TEST.mkv'");
                Docker("requestarr-dev-sonarr-test-1", $"mkdir -p '{seriesPath}/Season 01' && " + string.Join(" && ", aired.Select(n => $"truncate -s 900M '{seriesPath}/Season 01/Severance.S01E{n:00}.1080p.WEB.h264-TEST.mkv'")));
                Log($"[import] placed 1 movie file and {aired.Count} episode files (season 1, every aired episode); asking both to rescan");
                await Post(http, RadarrUrl, RadarrKey, "command", new JObject { ["name"] = "RescanMovie", ["movieId"] = movie.ExternalItemId });
                await Post(http, SonarrUrl, SonarrKey, "command", new JObject { ["name"] = "RescanSeries", ["seriesId"] = series.ExternalItemId });

                for (var i = 0; i < 30; i++)
                {
                    var hasMovie = (bool)(await Get(http, RadarrUrl, RadarrKey, $"movie/{movie.ExternalItemId}"))["hasFile"];
                    var withFiles = ((JArray)await Get(http, SonarrUrl, SonarrKey, $"episode?seriesId={series.ExternalItemId}")).Children<JObject>().Count(e => (int)e["seasonNumber"] == 1 && (bool)e["hasFile"]);

                    if (hasMovie && withFiles >= aired.Count)
                    {
                        Log($"[import] Radarr hasFile={hasMovie}; Sonarr season 1 files {withFiles}/{aired.Count}");
                        break;
                    }

                    await Task.Delay(2000);
                }

                // The backup poll finds both.
                await availability.CheckAllAsync();

                var userChannel = (global::Discord.IMessageChannel)await host.Client.Rest.GetChannelAsync(channel);

                foreach (var request in new[] { requests.Get(movie.Id), requests.Get(series.Id) })
                {
                    var message = await userChannel.GetMessageAsync(ulong.Parse(request.MessageId));
                    Log($"[available] request #{request.Id} {request.Label}: status={request.Status} progress={request.Progress} seasons available={string.Join(",", request.AvailableSeasons)}");
                    Log($"[available]   user message read back (reposted): \"{message.Content.Replace("\n", " | ")}\" buttons={Buttons(message).Count}");
                    Log($"[available]   card footer: \"{message.Embeds.FirstOrDefault()?.Footer?.Text ?? "none"}\" icon={(message.Embeds.FirstOrDefault()?.Footer?.IconUrl != null ? "TMDB logo" : "none")}; cast field: {message.Embeds.FirstOrDefault()?.Fields.Any(f => f.Name == DiscordText.FieldCast) == true}");
                    request.Status.Should().Be(RequestStatus.Available);
                    message.Content.Should().StartWith($"<@{target.Id}> {DiscordText.FinalReady}");
                }

                (await userChannel.GetMessageAsync(movieMessage)).Should().BeNull("the old message was deleted when it was reposted");
                (await userChannel.GetMessageAsync(tvMessage)).Should().BeNull();

                var cards = await LogFeed(host, feedChannel, "after the poll found them");
                cards.Should().HaveCount(2, "one card per request");
                cards.Should().OnlyContain(c => c.Embeds.First().Description.StartsWith(DiscordText.FinalReady));
                Log($"[audit] {string.Join("; ", Mocker.Resolve<IAuditRepository>().All().Where(a => a.Action.StartsWith("request.")).Select(a => $"{a.Action} #{a.TargetId}"))}");
            }
            finally
            {
                if (host.Client?.GetGuild(settings.Current.GuildId) is { } cleanupGuild)
                {
                    await cleanupGuild.BulkOverwriteApplicationCommandAsync(Array.Empty<global::Discord.ApplicationCommandProperties>());
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.sh reset-uat to restore the stack)");
            }
        }

        private static async Task<List<global::Discord.IMessage>> LogFeed(DiscordClientHost host, ulong feedChannel, string when)
        {
            var feed = (global::Discord.IMessageChannel)await host.Client.Rest.GetChannelAsync(feedChannel);
            var cards = (await feed.GetMessagesAsync(20).FlattenAsync()).Where(m => m.Author.IsBot && m.Embeds.Any()).OrderBy(m => m.Timestamp).ToList();
            Log($"[feed] {when}: {cards.Count} card(s), oldest first");

            foreach (var card in cards)
            {
                var embed = card.Embeds.First();
                Log($"[feed]   {embed.Footer?.Text}: \"{embed.Title}\" thumbnail={(embed.Thumbnail != null ? "yes" : "no")} width spacer={(embed.Image is { } image ? $"{image.Width}x{image.Height}" : "none")} \"{embed.Description.Replace("\n", " | ")}\" buttons=[{string.Join(", ", Buttons(card))}]");
            }

            return cards;
        }

        private static List<string> Buttons(global::Discord.IMessage message) =>
            message.Components.OfType<global::Discord.ActionRowComponent>().SelectMany(r => r.Components).OfType<global::Discord.ButtonComponent>().Select(b => b.Label).ToList();

        // Posts the request's message as the bot (as /request leaves it), then presses the final button on it.
        private static async Task<ulong> RequestInRealMessage(InteractionDispatcher dispatcher, IDiscordGuildGateway gateway, ulong guild, ulong channel, ulong user, string customId, string placeholder)
        {
            var message = await gateway.SendMessageAsync(channel, new InteractionReply { Content = placeholder });
            var responder = new RecordingResponder(null, channel, message, false);
            await dispatcher.DispatchAsync(FakeInteraction.Button(customId).By(user).InGuild(guild).InChannel(channel).OnMessage(message).Build(), responder);
            await gateway.EditMessageAsync(channel, message, responder.LastReply);
            Log($"[request] {customId} -> {responder.LastReply.Content.Replace("\n", " | ")}");

            return message;
        }

        private static async Task<InteractionRun> Send(InteractionDispatcher dispatcher, FakeInteraction interaction)
        {
            var responder = new RecordingResponder(null, interaction.Build().ChannelId, interaction.Build().MessageId, false);
            var outcome = await dispatcher.DispatchAsync(interaction.Build(), responder);
            return new InteractionRun(outcome, responder);
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

        private static void Docker(string container, string script)
        {
            var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "exec", container, "sh", "-c", script })
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            process.WaitForExit(60000);
            process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd());
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
