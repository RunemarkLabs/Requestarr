using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Categories;
using Requestarr.Core.Admin;
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

namespace Requestarr.Core.Test.AdminRequests
{
    // Live (task 3c): the real bot in the Discord test server with the dev stack's TEST Radarr and Sonarr.
    // Grants Music with no Lidarr (the private notice, and the member's real panel without Music); walks
    // two pending requests from two members and one Plex link through /admin requests; stops the test
    // Sonarr for longer than a shortened alert threshold, reads back the down alert and the paused
    // message, starts it again and reads back the recovered alert. Clicks go through the real handlers
    // (their replies are private, so they're recorded here); real effects are read back from Discord.
    // Never runs in CI (ManualTest). Needs the stack's Requestarr stopped: docker/dev.sh reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveAdminRequestsFixture : DbTest
    {
        private const int Matrix = 603;
        private const int Heat = 949;

        private static readonly TimeSpan ShortAlert = TimeSpan.FromSeconds(20);

        private static string RadarrUrl => LiveDev.RadarrUrl;
        private static string RadarrKey => LiveDev.RadarrKey;
        private static string SonarrUrl => LiveDev.SonarrUrl;
        private static string SonarrKey => LiveDev.SonarrKey;

        [Test]
        public async Task music_notice_walk_and_sonarr_outage_in_the_test_server()
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
            Mocker.SetConstant<Requestarr.Core.Removals.IRemovalProposalRepository>(Mocker.Resolve<Requestarr.Core.Removals.RemovalProposalRepository>());
            Mocker.SetConstant<Requestarr.Core.Removals.IRemovalFlagRepository>(Mocker.Resolve<Requestarr.Core.Removals.RemovalFlagRepository>());
            Mocker.SetConstant<Requestarr.Core.Removals.IRemovalPromptRepository>(Mocker.Resolve<Requestarr.Core.Removals.RemovalPromptRepository>());
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
            Mocker.SetConstant<IAdminQueue>(new AdminQueue(new IAdminQueueSource[]
            {
                Mocker.Resolve<PendingRequestsSource>(),
                Mocker.Resolve<TroubleSource>(),
                Mocker.Resolve<PlexLinkSource>(),
                Mocker.Resolve<DownInstanceSource>()
            }));
            var monitor = Mocker.Resolve<ConnectionMonitor>();
            monitor.AlertAfter = ShortAlert;
            Mocker.SetConstant<IConnectionMonitor>(monitor);
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            var handlers = new IInteractionHandler[] { Mocker.Resolve<AdminConsoleHandler>(), Mocker.Resolve<RequestHandler>(), Mocker.Resolve<ApprovalHandler>(), Mocker.Resolve<SeasonsHandler>(), Mocker.Resolve<MediaServerHandler>(), Mocker.Resolve<PanelHandler>(), Mocker.Resolve<AdminRequestsHandler>() };
            var dispatcher = new InteractionDispatcher(handlers, LogManager.GetLogger("LiveDiscord"));
            var sonarrStopped = false;

            try
            {
                await host.StartAsync(settings.Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

                var guild = host.Client.GetGuild(settings.Current.GuildId);
                var adminChannel = ulong.Parse(guildSetup.GetConfig().AdminChannelId);
                var adminRest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(adminChannel);
                FakeInteraction AsOwner(FakeInteraction i) => i.By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel);

                var registered = await guild.GetApplicationCommandsAsync();
                Log($"[commands] {string.Join(" ", registered.OrderBy(c => c.Name).Select(c => $"/{c.Name}({string.Join(", ", c.Options.Select(o => o.Name))})"))}");
                registered.Single(c => c.Name == "admin").Options.Select(o => o.Name).Should().Equal("user", "requests", "help");

                // Two test members, onboarded through their member cards.
                // Two named test members when there are two; otherwise both requests come from the one.
                var members = LiveTestMembers.Pick(await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync());
                Log($"[members] test member(s): {string.Join(", ", members.Select(m => m.Username))}");
                var (one, two) = (members[0], members.Count > 1 ? members[1] : members[0]);

                foreach (var member in new[] { one, two }.Distinct())
                {
                    var added = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Add}:{member.Id}")));
                    Log($"[add] {member.Username}: {added.LastReply.Content.Replace("\n", " | ")}");
                }

                var channelOne = ulong.Parse(users.FindByDiscordId(one.Id).PrivateChannelId);
                var channelTwo = ulong.Parse(users.FindByDiscordId(two.Id).PrivateChannelId);
                var restOne = (IMessageChannel)await host.Client.Rest.GetChannelAsync(channelOne);

                // 1. Grant Music with no Lidarr: the card changes in place, a private notice says why they won't see it.
                var card = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin user").WithOption("member", one.Id)));
                var menus = new[] { card.LastReply.SelectMenu }.Concat(card.LastReply.MoreMenus).ToList();
                Log($"[card] {one.Username}: {menus.Count} menus [{string.Join("] [", menus.Select(m => $"{m.Placeholder}: {string.Join("/", m.Options.Select(o => o.Default ? $"*{o.Label}*" : o.Label))}"))}] buttons [{string.Join("] [", card.LastReply.Buttons.Select(b => b.Label))}]");
                var music = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Access}:{one.Id}:music", "auto")));
                Log($"[music] responses: {string.Join(", ", music.Responder.Responses.Select(r => r.Kind))}");
                Log($"[music] card now: {music.Responder.Replies.First().Content.Replace("\n", " | ")}");
                Log($"[music] private notice: {music.LastReply.Content}");
                var panelOne = (await restOne.GetPinnedMessagesAsync()).Single();
                Log($"[music] {one.Username}'s pinned panel, read back: \"{panelOne.Content.Replace("\n", " | ")}\"");
                music.LastReply.Content.Should().Be(DiscordText.NotConnectedYet(one.Username, ContentType.Music));
                panelOne.Content.Should().NotContain("Music");

                // 2. Two pending requests from two members and one Plex link, walked through /admin requests.
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channelOne, one.Id, Matrix);
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channelTwo, two.Id, Heat);
                await Send(dispatcher, FakeInteraction.ModalSubmit(PanelRoutes.LinkPlexForm).WithField(PanelRoutes.PlexUsernameField, "live_walk_plex").By(one.Id).InGuild(guild.Id).InChannel(channelOne));

                var adminPanel = (await adminRest.GetPinnedMessagesAsync()).Single();
                Log($"[panel] admin panel buttons, read back: [{string.Join("] [", Buttons(adminPanel))}]");
                Buttons(adminPanel).Should().Contain(DiscordText.PanelPendingQueueCount(3));

                var first = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin requests")));
                LogCard("walk 1", first.LastReply);
                first.LastReply.Content.Should().StartWith(DiscordText.WalkHeader(1, 3));
                var second = await Press(dispatcher, AsOwner, first.LastReply, DiscordText.ApproveLabel);
                LogCard("walk 2", second.LastReply);
                second.LastReply.Content.Should().StartWith(DiscordText.WalkHeader(1, 2));
                var form = await Press(dispatcher, AsOwner, second.LastReply, DiscordText.DenyLabel);
                Log($"[walk] Deny opened the form {form.Responder.LastModal?.CustomId}");
                var third = await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId).WithField(RequestRoutes.ReasonField, "live walk check")));
                LogCard("walk 3", third.LastReply);
                third.LastReply.Buttons.Select(b => b.Label).Should().Contain(DiscordText.ApproveLinkLabel);
                var done = await Press(dispatcher, AsOwner, third.LastReply, DiscordText.ApproveLinkLabel);
                Log($"[walk] end: {done.LastReply.Content.Replace("\n", " | ")}");
                done.LastReply.Content.Should().EndWith(DiscordText.WalkDone);

                var requests = Mocker.Resolve<IRequestRepository>();
                foreach (var r in requests.All())
                {
                    var channel = (IMessageChannel)await host.Client.Rest.GetChannelAsync(r.OwnerUserId == users.FindByDiscordId(one.Id).Id ? channelOne : channelTwo);
                    var message = await channel.GetMessageAsync(ulong.Parse(r.MessageId));
                    Log($"[walk] request #{r.Id} {r.Label} status={r.Status}; the user's message, read back: \"{message.Content.Replace("\n", " | ")}\"");
                }

                requests.All().Select(r => r.Status).Should().BeEquivalentTo(new[] { RequestStatus.Approved, RequestStatus.Denied });
                Log($"[walk] {one.Username}'s Plex account now: {Mocker.Resolve<IMediaServerAccountRepository>().GetForUser(users.FindByDiscordId(one.Id).Id).Single().Username}");
                adminPanel = (await adminRest.GetPinnedMessagesAsync()).Single();
                Log($"[panel] admin panel buttons after the walk: [{string.Join("] [", Buttons(adminPanel))}]");

                // 3. Stop the test Sonarr past the shortened threshold: one down alert, and TV requests say they're paused.
                await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Access}:{one.Id}:tv", "auto")));
                Docker("stop");
                sonarrStopped = true;
                var started = DateTime.UtcNow;
                await monitor.CheckAllAsync();
                Log($"[outage] Sonarr stopped; first failed check at {started:HH:mm:ss}; alert threshold {ShortAlert.TotalSeconds}s");
                await Task.Delay(ShortAlert + TimeSpan.FromSeconds(5));
                await monitor.CheckAllAsync();
                await monitor.CheckAllAsync();

                var sonarr = Mocker.Resolve<IBackendRepository>().All().Single(b => b.Name == "Sonarr (test)");
                sonarr.DownAlertMessageId.Should().NotBeNull("the outage passed the threshold");
                var alert = await adminRest.GetMessageAsync(ulong.Parse(sonarr.DownAlertMessageId));
                Log($"[outage] down alert, read back: \"{alert.Content}\"");
                var downAlerts = (await adminRest.GetMessagesAsync(20).FlattenAsync()).Count(m => m.Content.StartsWith("⚠️ **Sonarr (test) can't be reached**"));
                Log($"[outage] down alerts in the admin channel: {downAlerts}");
                downAlerts.Should().Be(1);

                var paused = await Send(dispatcher, FakeInteraction.SlashCommand("request").WithOption("type", "tv").WithOption("query", "severance").By(one.Id).InGuild(guild.Id).InChannel(channelOne));
                Log($"[outage] {one.Username}'s /request tv severance: {paused.LastReply.Content.Replace("\n", " | ")}");
                paused.LastReply.Content.Should().Contain(DiscordText.RequestsPaused(ContentType.Tv, "Sonarr (test)"));
                panelOne = (await restOne.GetPinnedMessagesAsync()).Single();
                Log($"[outage] {one.Username}'s panel still lists TV: {panelOne.Content.Contains("TV")}; access TV={Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(users.FindByDiscordId(one.Id).Id).Single(a => a.ContentType == ContentType.Tv).State}");
                var walkDown = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin requests")));
                LogCard("outage walk", walkDown.LastReply);

                // 4. Start it again: one recovered alert with how long it was down.
                Docker("start");
                sonarrStopped = false;

                for (var i = 0; i < 40 && Mocker.Resolve<IBackendRepository>().Get(sonarr.Id).DownSince != null; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    await monitor.CheckAllAsync();
                }

                var back = (await adminRest.GetMessagesAsync(10).FlattenAsync()).Where(m => m.Content.StartsWith("✅ **Sonarr (test) is back**")).ToList();
                Log($"[outage] recovered alert(s), read back: {string.Join(" / ", back.Select(m => $"\"{m.Content}\""))}");
                back.Should().ContainSingle();
                Mocker.Resolve<IBackendRepository>().Get(sonarr.Id).DownSince.Should().BeNull();
                var works = await Send(dispatcher, FakeInteraction.SlashCommand("request").WithOption("type", "tv").WithOption("query", "severance").By(one.Id).InGuild(guild.Id).InChannel(channelOne));
                Log($"[outage] after recovery, /request tv severance: {works.LastReply.Content.Split('\n')[0]}");
                works.LastReply.Content.Should().NotContain("paused");
            }
            finally
            {
                if (sonarrStopped)
                {
                    Docker("start");
                }

                if (host.Client?.GetGuild(settings.Current.GuildId) is { } cleanupGuild)
                {
                    await cleanupGuild.BulkOverwriteApplicationCommandAsync(Array.Empty<ApplicationCommandProperties>());
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.sh reset-dev to restore the dev stack)");
            }
        }

        // docker stop/start on the dev stack's test Sonarr, found by its compose service name.
        private static void Docker(string verb)
        {
            var id = Run("ps", "-aq", "--filter", "label=com.docker.compose.service=sonarr-test").Trim();
            id.Should().NotBeEmpty("the dev stack's test Sonarr must exist");
            Run(verb, id);
            Log($"[docker] {verb} sonarr-test");
        }

        private static string Run(params string[] args)
        {
            var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd());
            return output;
        }

        private static Task<InteractionRun> Press(InteractionDispatcher dispatcher, Func<FakeInteraction, FakeInteraction> asOwner, InteractionReply card, string label) =>
            Send(dispatcher, asOwner(FakeInteraction.Button(card.Buttons.Single(b => b.Label == label).CustomId)));

        private static void LogCard(string tag, InteractionReply card) =>
            Log($"[{tag}] {card.Content.Replace("\n", " | ")}{(card.Embeds?.Count > 0 ? $" embed=\"{card.Embeds[0].Title}\"" : string.Empty)} buttons=[{string.Join("] [", card.Buttons.Select(b => b.Label))}]");

        private static List<string> Buttons(IMessage message) =>
            message.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => b.Label).ToList();

        // Posts the request's message as the bot (as /request leaves it), then presses Request on it.
        private static async Task RequestInRealMessage(InteractionDispatcher dispatcher, IDiscordGuildGateway gateway, ulong guild, ulong channel, ulong user, int tmdbId)
        {
            var message = await gateway.SendMessageAsync(channel, new InteractionReply { Content = $"🎬 **Movies request** · details (tmdb:{tmdbId})" });
            var responder = new RecordingResponder(null, channel, message, false);
            await dispatcher.DispatchAsync(FakeInteraction.Button($"req-make:movie:{tmdbId}").By(user).InGuild(guild).InChannel(channel).OnMessage(message).Build(), responder);
            await gateway.EditMessageAsync(channel, message, responder.LastReply);
            Log($"[request] tmdb:{tmdbId} -> {responder.LastReply.Content.Replace("\n", " | ")}");
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
