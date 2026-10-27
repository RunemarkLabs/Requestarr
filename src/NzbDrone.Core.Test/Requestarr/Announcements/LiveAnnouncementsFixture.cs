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
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Announcements
{
    // Live (task 3d): the real bot in the Discord test server with the dev stack's TEST Radarr and Sonarr.
    // Reads back the announcements channel, its permissions and the members role on a real member, then
    // posts what's new for the current version and for a simulated upgrade; stops the test Sonarr past
    // shortened thresholds, presses Announce to users and reads back the notice, then starts it again
    // and reads back the recovery notice. Never runs in CI (ManualTest). Needs the stack's Requestarr
    // stopped: docker/dev.sh reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveAnnouncementsFixture : DbTest
    {
        // LIVE_REAL_THRESHOLDS=1 waits the product's real 3 and 30 minutes instead.
        private static readonly bool Real = Environment.GetEnvironmentVariable("LIVE_REAL_THRESHOLDS") == "1";
        private static readonly TimeSpan ShortAlert = Real ? ConnectionMonitor.DefaultAlertAfter : TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ShortAnnounce = Real ? ConnectionMonitor.DefaultAnnounceAfter : TimeSpan.FromSeconds(30);

        private static string RadarrUrl => LiveDev.RadarrUrl;
        private static string RadarrKey => LiveDev.RadarrKey;
        private static string SonarrUrl => LiveDev.SonarrUrl;
        private static string SonarrKey => LiveDev.SonarrKey;

        [Test]
        public async Task announcements_channel_outage_and_upgrade_in_the_test_server()
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
                Mocker.Resolve<DownInstanceSource>()
            }));

            // The dev server keeps only the version running now (task 4d), as the dev stack does.
            Mocker.SetConstant<IReleaseEnvironment>(new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.EnvironmentVariable ? "dev" : null });
            var announcements = Mocker.Resolve<AnnouncementService>();
            Mocker.SetConstant<IAnnouncementService>(announcements);
            var monitor = Mocker.Resolve<ConnectionMonitor>();
            monitor.AlertAfter = ShortAlert;
            monitor.AnnounceAfter = ShortAnnounce;
            Mocker.SetConstant<IConnectionMonitor>(monitor);
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            var handlers = new IInteractionHandler[] { Mocker.Resolve<AdminConsoleHandler>(), Mocker.Resolve<RequestHandler>(), Mocker.Resolve<ApprovalHandler>(), Mocker.Resolve<SeasonsHandler>(), Mocker.Resolve<MediaServerHandler>(), Mocker.Resolve<PanelHandler>(), Mocker.Resolve<AdminRequestsHandler>(), Mocker.Resolve<OutageHandler>() };
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
                var restGuild = await host.Client.Rest.GetGuildAsync(guild.Id);
                string Name(ulong id) => id switch
                {
                    _ when id == guild.Id => "@everyone",
                    _ when id == host.Client.CurrentUser.Id => "the bot",
                    _ => restGuild.GetRole(id)?.Name ?? id.ToString()
                };

                // 1. The channel and its permissions, read back from Discord.
                var membersRole = guildSetup.MembersRoleId.Value;
                var channel = (global::Discord.Rest.RestTextChannel)await host.Client.Rest.GetChannelAsync(guildSetup.AnnouncementsChannelId.Value);
                Log($"[channel] #{channel.Name} (category: {(channel.CategoryId.HasValue ? channel.CategoryId.ToString() : "none")}) topic: \"{channel.Topic}\"");

                foreach (var o in channel.PermissionOverwrites)
                {
                    Log($"[channel]   {Name(o.TargetId)}: allow [{string.Join(", ", o.Permissions.ToAllowList())}] deny [{string.Join(", ", o.Permissions.ToDenyList())}]");
                }

                var members = channel.PermissionOverwrites.Single(o => o.TargetId == membersRole).Permissions;
                members.ViewChannel.Should().Be(PermValue.Allow);
                members.SendMessages.Should().Be(PermValue.Deny);
                members.CreatePublicThreads.Should().Be(PermValue.Deny);
                channel.PermissionOverwrites.Single(o => o.TargetId == guild.Id).Permissions.ViewChannel.Should().Be(PermValue.Deny);
                channel.PermissionOverwrites.Single(o => o.TargetId == guildSetup.AdminRoleId.Value).Permissions.SendMessages.Should().Be(PermValue.Allow);
                Log($"[role] {Name(membersRole)}: roles in the server now {restGuild.Roles.Count}");

                // 2. Membership on a real member: add, suspend, unsuspend.
                var member = LiveTestMembers.Pick(await restGuild.GetUsersAsync().FlattenAsync())[0];
                async Task<bool> HasRole() => (await host.Client.Rest.GetGuildUserAsync(guild.Id, member.Id)).RoleIds.Contains(membersRole);
                await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Add}:{member.Id}")));
                var afterAdd = await HasRole();
                await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Suspend}:{member.Id}")));
                var afterSuspend = await HasRole();
                await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Unsuspend}:{member.Id}")));
                var afterUnsuspend = await HasRole();
                Log($"[members] {member.Username} holds {Name(membersRole)}: after add {afterAdd}, after suspend {afterSuspend}, after unsuspend {afterUnsuspend}");
                (afterAdd, afterSuspend, afterUnsuspend).Should().Be((true, false, true));

                // 3. What's new on the dev server: only the current version, then a simulated upgrade replaces it, and a
                // restart changes nothing (task 4d: dev shows only the version running now).
                async Task<List<IMessage>> Recent(IMessageChannel c) => (await c.GetMessagesAsync(30).FlattenAsync()).OrderBy(m => m.Id).ToList();
                var current = Changelog.Release(announcements.CurrentVersion());
                await announcements.PostVersionNotesAsync();

                // Since task 4e each version is a one-line card, found by its title.
                async Task<List<string>> Cards(IMessageChannel c, VersionAudience audience) =>
                    (await Recent(c)).Select(m => m.Embeds.FirstOrDefault()?.Title).Where(t => VersionCards.Parse(t, audience) != null).ToList();
                var adminNotes = await Cards(adminRest, VersionAudience.Admins);
                var userNotes = await Cards(channel, VersionAudience.Users);
                Log($"[upgrade] start on {current}: {adminNotes.Count} admin card(s), {userNotes.Count} user card(s)");
                adminNotes.ForEach(t => Log($"[upgrade]   admin, read back: \"{t}\""));
                userNotes.ForEach(t => Log($"[upgrade]   users, read back: \"{t}\""));
                var running = Changelog.Parse(announcements.ChangelogText()).SingleOrDefault(v => v.Version == current);
                adminNotes.Should().HaveCount(running?.HasNews == true ? 1 : 0, "only the version running now, and a version with nothing for anyone posts nothing");
                adminNotes.Should().OnlyContain(t => t.StartsWith($"Requestarr {current} · "));
                var hasUserLines = running?.Users.Count > 0;
                userNotes.Should().HaveCount(hasUserLines ? 1 : 0, "users only hear about versions with changes for them");

                var next = new Version(current.Major, current.Minor, current.Build + 1);
                announcements.CurrentVersion = () => new Version(next.Major, next.Minor, next.Build, 0);
                var shipped = announcements.ChangelogText();
                announcements.ChangelogText = () => $"## {next}\n\n### For users\n- Live check: this is how something new is announced to you.\n\n### For admins\n- Live check: admins get the full list.\n\n" + shipped;
                await announcements.PostVersionNotesAsync();
                await announcements.PostVersionNotesAsync();
                var adminAfter = await Cards(adminRest, VersionAudience.Admins);
                var usersAfter = await Cards(channel, VersionAudience.Users);
                Log($"[upgrade] simulated upgrade to {next}, started twice: admin cards {adminAfter.Count}, user cards {usersAfter.Count}; stored version {guildSetup.GetConfig().AnnouncedVersion}");
                adminAfter.ForEach(t => Log($"[upgrade]   admin, read back: \"{t}\""));
                usersAfter.ForEach(t => Log($"[upgrade]   users, read back: \"{t}\""));
                adminAfter.Should().ContainSingle("the new version replaces the old card, once, even across restarts")
                    .Which.Should().Be($"Requestarr {next} · 1 change for admins, 1 for users");
                usersAfter.Should().ContainSingle().Which.Should().Be($"Requestarr {next} · 1 new thing for you");

                // 4. Stop the test Sonarr past the shortened thresholds; press Announce to users.
                Docker("stop");
                sonarrStopped = true;
                var sonarr = Mocker.Resolve<IBackendRepository>().All().Single(b => b.Name == "Sonarr (test)");
                Log($"[outage] Sonarr stopped; alert after {ShortAlert.TotalSeconds}s, Announce offered after {ShortAnnounce.TotalSeconds}s {(Real ? "(the product defaults)" : "(shortened on the monitor; defaults 3 and 30 minutes)")}");

                for (var i = 0; i < 480 && !Mocker.Resolve<IBackendRepository>().Get(sonarr.Id).DownAnnounceOffered; i++)
                {
                    await monitor.CheckAllAsync();
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }

                sonarr = Mocker.Resolve<IBackendRepository>().Get(sonarr.Id);
                var alert = await adminRest.GetMessageAsync(ulong.Parse(sonarr.DownAlertMessageId));
                Log($"[outage] down alert, read back: \"{alert.Content.Replace("\n", " | ")}\" buttons=[{string.Join("] [", Buttons(alert))}] pinned: {alert.IsPinned}");
                alert.IsPinned.Should().BeTrue("pinned while the outage lasts");
                Buttons(alert).Should().Equal(DiscordText.AnnounceLabel);
                var customId = alert.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Single().CustomId;

                var pressed = await Send(dispatcher, AsOwner(FakeInteraction.Button(customId)).OnMessage(alert.Id));
                Log($"[outage] pressed {DiscordText.AnnounceLabel}; private reply: {pressed.LastReply.Content}");
                sonarr = Mocker.Resolve<IBackendRepository>().Get(sonarr.Id);
                var notice = await channel.GetMessageAsync(ulong.Parse(sonarr.DownAnnounceMessageId));
                Log($"[outage] announcement, read back: \"{notice.Content.Replace("\n", " | ")}\"");
                alert = await adminRest.GetMessageAsync(alert.Id);
                Log($"[outage] alert now: \"{alert.Content.Replace("\n", " | ")}\" buttons=[{string.Join("] [", Buttons(alert))}]");
                notice.Content.Should().Be(DiscordText.OutageNotice(ContentType.Tv, false));
                notice.IsPinned.Should().BeTrue();
                Buttons(alert).Should().BeEmpty();
                var noticeId = notice.Id;
                var alertId = alert.Id;

                // 5. Start it again: the recovery is announced because the outage was.
                Docker("start");
                sonarrStopped = false;

                for (var i = 0; i < 40 && Mocker.Resolve<IBackendRepository>().Get(sonarr.Id).DownSince != null; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    await monitor.CheckAllAsync();
                }

                var posts = await Recent(channel);
                Log($"[outage] recovery, read back: \"{posts.Last().Content.Replace("\n", " | ")}\"");
                Log($"[outage] admin channel: \"{(await Recent(adminRest)).Last().Content}\"");
                posts.Last().Content.Should().StartWith("✅ **TV requests work again**");
                var noticeGone = await channel.GetMessageAsync(noticeId) == null;
                var alertGone = await adminRest.GetMessageAsync(alertId) == null;
                var outage = Mocker.Resolve<IOutageRepository>().History().First();
                Log($"[outage] after recovery: notice removed {noticeGone}, alert removed {alertGone}, \"back\" pinned {posts.Last().IsPinned}; outage record: {outage.InstanceName} {outage.Started:HH:mm:ss}Z to {outage.Ended:HH:mm:ss}Z, {outage.Duration?.TotalSeconds:0}s, announced by {outage.AnnouncedBy}");
                (noticeGone, alertGone, posts.Last().IsPinned).Should().Be((true, true, false));
                (outage.Ended != null, outage.AnnouncedBy).Should().Be((true, "owner"));
                Log($"[channel] #{channel.Name} now holds {posts.Count} message(s): {string.Join(" / ", posts.Select(p => p.Content.Split('\n')[0]))}");
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

        // docker stop/start on the dev stack's test Sonarr, found by its compose project and service (never the QA stack's).
        private static void Docker(string verb)
        {
            var id = Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={LiveDev.ComposeProject}", "--filter", "label=com.docker.compose.service=sonarr-test").Trim();
            id.Should().NotBeEmpty("the dev stack's test Sonarr must exist");
            id.Should().NotContain("\n", "exactly one container");
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

        private static List<string> Buttons(IMessage message) =>
            message.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => b.Label).ToList();

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
