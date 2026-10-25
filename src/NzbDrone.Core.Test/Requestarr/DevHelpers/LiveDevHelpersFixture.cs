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

namespace Requestarr.Core.Test.DevHelpers
{
    // Live (task 4b): the dev helpers with the real dev bot in the dev Discord server and the dev stack's TEST
    // Radarr and Sonarr. The test member requests Dune and Severance; docker\dev.ps1 fake-download (run here
    // from a fresh Windows PowerShell) makes them look downloaded, and Requestarr's poll finds them (Available,
    // partly available). Then, with fast votes on (the same switch the dev stack's container reads, turned on
    // here for this in-process Requestarr), a user-started removal closes on its own and an admin removal goes
    // through the requester's reminder and silence to the vote, in real minutes. Only the test accounts in
    // DISCORD_TEST_MEMBERS. Never runs in CI (ManualTest). Needs the stack's Requestarr stopped:
    // docker\dev.ps1 reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveDevHelpersFixture : DbTest
    {
        private const int Dune = 438631;
        private const int Severance = 371980;

        private static string RadarrUrl => LiveDev.RadarrUrl;
        private static string RadarrKey => LiveDev.RadarrKey;
        private static string SonarrUrl => LiveDev.SonarrUrl;
        private static string SonarrKey => LiveDev.SonarrKey;

        [Test]
        public async Task fake_downloads_and_fast_votes_in_the_test_server()
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

            // Fast votes on, as the dev stack's container reads them: the dev-stack variable and the marker file.
            var appData = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "requestarr-devhelpers-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(appData);
            System.IO.File.WriteAllText(System.IO.Path.Combine(appData, DevFastVotes.MarkerFile), string.Empty);
            var folders = new Moq.Mock<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
            folders.SetupGet(f => f.AppDataFolder).Returns(appData);
            Mocker.SetConstant<IDevFastVotes>(new DevFastVotes(folders.Object) { Variable = name => name == DevFastVotes.DevStackVariable ? "true" : null });
            var settingsStore = Mocker.Resolve<RemovalSettings>();
            Mocker.SetConstant<IRemovalSettings>(settingsStore);
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
                var proposals = Mocker.Resolve<IRemovalProposalRepository>();

                // The only test account: requests as it. Everyone else in the server is left alone.
                var member = LiveTestMembers.Pick(await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync())[0];
                FakeInteraction AsMember(FakeInteraction i, ulong channel) => i.By(member.Id, member.Username).InGuild(guild.Id).InChannel(channel);
                await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Add}:{member.Id}")));
                await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Access}:{member.Id}:movie", "auto")));
                await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Access}:{member.Id}:tv", "auto")));
                var channel = ulong.Parse(users.FindByDiscordId(member.Id).PrivateChannelId);
                var rest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(channel);
                Log($"[setup] {member.Username} added with Movies and TV auto-approve");

                async Task<string> Message(Request r) => (await rest.GetMessageAsync(ulong.Parse(requests.Get(r.Id).MessageId))).Content.Replace("\n", " | ");
                async Task<string> Card(Request r) => (await feedRest.GetMessageAsync(ulong.Parse(requests.Get(r.Id).FeedMessageId))).Embeds.First().Description.Replace("\n", " | ");
                async Task<List<string>> Recent(int count) => (await rest.GetMessagesAsync(count).FlattenAsync()).OrderBy(m => m.Id).Select(m => m.Content.Replace("\n", " | ")).ToList();

                // 1. fake-download movie "Dune" after the test member requested Dune.
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, member.Id, $"req-make:movie:{Dune}", $"🎬 **Movies request** · details (tmdb:{Dune})");
                var dune = requests.All().Single(r => r.TmdbId == Dune);
                DevPs1("fake-download", "movie", "Dune");
                await availability.CheckAllAsync();
                Log($"[movie] request #{dune.Id} status={requests.Get(dune.Id).Status}; {member.Username}'s request message, read back: \"{await Message(dune)}\"");
                Log($"[movie] feed card, read back: \"{await Card(dune)}\"");
                requests.Get(dune.Id).Status.Should().Be(RequestStatus.Available);

                // 2. fake-download tv "Severance" 1, for a request of seasons 1 and 2: partly available.
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, member.Id, $"req-tv-ok:{Severance}:1-2", $"📺 **TV request** · details (tvdb:{Severance})");
                var show = requests.All().Single(r => r.TvdbId == Severance);
                DevPs1("fake-download", "tv", "Severance", "1");
                await availability.CheckAllAsync();
                Log($"[tv] request #{show.Id} status={requests.Get(show.Id).Status}, seasons there: {string.Join(", ", requests.Get(show.Id).AvailableSeasons)}; {member.Username}'s request message, read back: \"{await Message(show)}\"");
                Log($"[tv] feed card, read back: \"{await Card(show)}\"");
                requests.Get(show.Id).AvailableSeasons.Should().Equal(1);

                // 3. Fast votes, in real minutes: a user-started removal of Dune that nobody keeps, and the
                //    admin's removal of Severance that its requester never answers.
                Log($"[fast] fast votes on: vote {settingsStore.VoteWindow.TotalMinutes} min, requester {settingsStore.RequesterTimeout.TotalMinutes} min, reminder after {settingsStore.ReminderAfter?.TotalMinutes} min");
                await removals.TickAsync();
                var notice = (await ((IMessageChannel)await host.Client.Rest.GetChannelAsync(adminChannel)).GetMessagesAsync(5).FlattenAsync()).First(m => m.Content.StartsWith("🧪"));
                Log($"[fast] admin channel, read back: \"{notice.Content}\"");

                var userCard = await Send(dispatcher, AsMember(FakeInteraction.Button($"{RemovalRoutes.User}:{dune.Id}"), channel));
                await Send(dispatcher, AsMember(FakeInteraction.Button(userCard.LastReply.Buttons.Single().CustomId), channel));
                var adminCard = await Send(dispatcher, InFeed(FakeInteraction.Button($"{RemovalRoutes.Feed}:{show.Id}")).OnMessage(ulong.Parse(requests.Get(show.Id).FeedMessageId)));
                await Send(dispatcher, InFeed(FakeInteraction.Button(adminCard.LastReply.Buttons.Single(b => b.Label == DiscordText.StartRemovalLabel).CustomId)));
                var started = DateTime.UtcNow;
                var duneVote = proposals.All().Single(p => p.TmdbId == Dune);
                var showVote = proposals.All().Single(p => p.TvdbId == Severance);
                Log($"[fast] {started:HH:mm:ss}Z Dune: vote closes {duneVote.WindowClosesAt:HH:mm:ss}Z; Severance: {member.Username} asked, answer by {showVote.AnswerBy:HH:mm:ss}Z");
                Log($"[fast] {member.Username}'s channel now: {string.Join(" // ", await Recent(2))}");

                var seen = new HashSet<string>();
                for (var i = 0; i < 80 && proposals.Get(showVote.Id).Status != RemovalStatus.Deleted; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    await removals.TickAsync();

                    foreach (var p in new[] { proposals.Get(duneVote.Id), proposals.Get(showVote.Id) })
                    {
                        var state = $"{p.Label}: {p.Status}{(p.ReminderSent ? " (reminded)" : string.Empty)}{(p.RequesterResponse.HasValue ? $" (requester {p.RequesterResponse})" : string.Empty)}";

                        if (seen.Add(state))
                        {
                            Log($"[fast] {DateTime.UtcNow:HH:mm:ss}Z (+{(DateTime.UtcNow - started).TotalMinutes:0.0} min) {state}");
                        }
                    }
                }

                var messages = (await rest.GetMessagesAsync(12).FlattenAsync()).OrderBy(m => m.Id).ToList();
                foreach (var m in messages.Where(m => m.Content.Contains("Reminder") || m.Content.Contains("No answer came") || m.Content.Contains("Still watching") || m.Content.Contains("Removed")))
                {
                    Log($"[fast] {m.Timestamp:HH:mm:ss}Z {member.Username}'s channel, read back: \"{m.Content.Replace("\n", " | ")}\"");
                }

                var http = Mocker.Resolve<IHttpClient>();
                var radarrDune = ((JArray)await Get(http, RadarrUrl, RadarrKey, $"movie?tmdbId={Dune}")).Count;
                var sonarrShow = ((JArray)await Get(http, SonarrUrl, SonarrKey, "series")).Count(x => (int?)x["tvdbId"] == Severance);
                Log($"[fast] removals: Dune {proposals.Get(duneVote.Id).Status}, Severance {proposals.Get(showVote.Id).Status}; read back: Dune in the test Radarr {radarrDune}, Severance in the test Sonarr {sonarrShow}");
                Log($"[fast] feed cards, read back: Dune \"{await Card(dune)}\" // Severance \"{await Card(show)}\"");
                proposals.Get(duneVote.Id).Status.Should().Be(RemovalStatus.Deleted);
                proposals.Get(showVote.Id).Status.Should().Be(RemovalStatus.Deleted);
                proposals.Get(showVote.Id).RequesterResponse.Should().Be(RequesterResponse.Silent);
                proposals.Get(showVote.Id).ReminderSent.Should().BeTrue();
            }
            finally
            {
                if (host.Client?.GetGuild(settings.Current.GuildId) is { } cleanupGuild)
                {
                    await cleanupGuild.BulkOverwriteApplicationCommandAsync(Array.Empty<ApplicationCommandProperties>());
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.ps1 reset-dev to restore the dev stack)");
            }
        }

        // Runs docker\dev.ps1 on the dev stack from a fresh Windows PowerShell with the owner's script policy, and logs its output.
        private static void DevPs1(params string[] arguments)
        {
            arguments = new[] { "-Stack", LiveDev.Stack }.Concat(arguments).ToArray();
            var script = System.IO.Path.Combine(AnimeDub.Item4To6Fixture.RepoFile("docker"), "dev.ps1");
            var output = Run(new[] { "powershell", "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", script }.Concat(arguments).ToArray());
            Log($"[dev.ps1] PS> .\\docker\\dev.ps1 {string.Join(" ", arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Log($"[dev.ps1]   {line.TrimEnd('\r')}");
            }
        }

        private static async Task<JToken> Get(IHttpClient http, string url, string key, string resource)
        {
            var request = new HttpRequestBuilder($"{url}/api/v3/{resource}").SetHeader("X-Api-Key", key).Build();
            return JToken.Parse((await http.GetAsync(request)).Content);
        }

        private static string Run(params string[] args)
        {
            var start = new ProcessStartInfo(args[0]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args.Skip(1))
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(300000);
            process.ExitCode.Should().Be(0, output);
            return output;
        }

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
