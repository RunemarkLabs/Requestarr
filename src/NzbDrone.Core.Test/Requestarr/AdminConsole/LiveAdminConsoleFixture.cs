using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NLog;
using NUnit.Framework;
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

namespace Requestarr.Core.Test.AdminConsole
{
    // Live: the real bot in the Discord test server with the dev stack's TEST Radarr. Reads back the
    // registered commands; onboards a member through the admin panel's member picker; changes their
    // access and limit on the member card; approves their request from /admin requests. Clicks are
    // simulated through the real handlers (their replies are private, so they're recorded here);
    // the real effects (role, channel, panel, request message) are read back from Discord.
    // Never runs in CI (ManualTest). Needs the stack's Requestarr stopped and a clean server:
    // docker/dev.sh reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveAdminConsoleFixture : DbTest
    {
        private const int Matrix = 603;

        private static string RadarrUrl => LiveDev.RadarrUrl;
        private static string RadarrKey => LiveDev.RadarrKey;

        [Test]
        public async Task commands_member_card_and_request_card_in_the_test_server()
        {
            var token = LiveDev.Token;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(RadarrKey))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN / RADARR_TEST_API_KEY not set");
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
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr (test)", Url = RadarrUrl, ApiKey = RadarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true } }
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
            Mocker.SetConstant<Requestarr.Core.MediaServers.IMediaServerConnections>(new FakeMediaServers { Servers = { MediaServerProvider.Plex } }); // the Plex link part needs Link Plex
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
            var handlers = new IInteractionHandler[] { Mocker.Resolve<AdminConsoleHandler>(), Mocker.Resolve<RequestHandler>(), Mocker.Resolve<ApprovalHandler>(), Mocker.Resolve<SeasonsHandler>(), Mocker.Resolve<MediaServerHandler>(), Mocker.Resolve<PanelHandler>(), Mocker.Resolve<AdminRequestsHandler>() };
            var dispatcher = new InteractionDispatcher(handlers, LogManager.GetLogger("LiveDiscord"));

            try
            {
                await host.StartAsync(settings.Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

                var guild = host.Client.GetGuild(settings.Current.GuildId);
                var adminChannel = ulong.Parse(guildSetup.GetConfig().AdminChannelId);
                FakeInteraction AsOwner(FakeInteraction i) => i.By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel);

                // 1. What Discord has registered.
                var registered = await guild.GetApplicationCommandsAsync();
                foreach (var command in registered.OrderBy(c => c.Name))
                {
                    var subs = command.Options.Select(o => $"{o.Name}({string.Join(", ", o.Options.Select(x => $"{x.Name}:{x.Type}{(x.IsAutocomplete == true ? ", autocomplete" : string.Empty)}"))})");
                    Log($"[commands] /{command.Name} default permission={command.DefaultMemberPermissions.RawValue} subcommands=[{string.Join(" ", subs)}]");
                }

                registered.Select(c => c.Name).Should().BeEquivalentTo("admin", "help", "request");
                registered.Single(c => c.Name == "admin").Options.Select(o => o.Name).Should().Equal("user", "requests", "help");

                // 2. The admin panel's buttons, read back from the panel (the newest message, unpinned since task 4e).
                var adminRest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(adminChannel);
                var panel = await adminRest.GetMessageAsync(ulong.Parse(guildSetup.GetConfig().AdminPanelMessageId));
                var panelButtons = panel.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => b.Label).ToList();
                Log($"[panel] admin panel buttons: [{string.Join("] [", panelButtons)}]");
                panelButtons.Should().Contain(DiscordText.PanelMembers);

                // 3. Onboard a member through the member picker.
                var members = await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync();
                var target = LiveTestMembers.Pick(members)[0];
                var picker = await Send(dispatcher, AsOwner(FakeInteraction.Button(AdminRoutes.Pick)));
                Log($"[picker] {picker.LastReply.Content} menu kind={picker.LastReply.SelectMenu.Kind} id={picker.LastReply.SelectMenu.CustomId}");
                var card = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu(AdminRoutes.Member, target.Id.ToString())));
                Log($"[card] {target.Username} before: \"{card.LastReply.Content.Replace("\n", " | ")}\" buttons=[{string.Join("] [", card.LastReply.Buttons.Select(b => b.Label))}]");
                var added = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Add}:{target.Id}")));
                Log($"[add] {added.LastReply.Content.Replace("\n", " | ")}");

                var user = users.FindByDiscordId(target.Id);
                var channel = (global::Discord.Rest.RestTextChannel)await host.Client.Rest.GetChannelAsync(ulong.Parse(user.PrivateChannelId));
                var role = guild.GetRole(ulong.Parse(user.DiscordRoleId));
                Log($"[discord] channel #{channel.Name}, role {role?.Name}, panel: \"{(await channel.GetMessageAsync(ulong.Parse(Mocker.Resolve<IRequestarrUserRepository>().Get(user.Id).PanelMessageId))).Content.Replace("\n", " | ")}\"");

                // 4. Access and a limit from the member card.
                var access = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Access}:{target.Id}:movie", "approval")));
                Log($"[access] {access.LastReply.Content}");
                await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.More}:{target.Id}", "limit:movie")));
                var limit = await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit($"{AdminRoutes.LimitForm}:{target.Id}:movie").WithField(AdminRoutes.LimitField, "5").WithField(AdminRoutes.DaysField, "7")));
                Log($"[limit] {limit.LastReply.Content}");
                var refreshed = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin user").WithOption("member", target.Id)));
                Log($"[card] after: \"{refreshed.LastReply.Content.Replace("\n", " | ")}\"");
                refreshed.LastReply.Content.Should().Contain("Movies: needs approval (quota 5 per 7 days)");
                var userPanel = (await channel.GetMessageAsync(ulong.Parse(Mocker.Resolve<IRequestarrUserRepository>().Get(user.Id).PanelMessageId))).Content;
                Log($"[discord] their panel now: \"{userPanel.Replace("\n", " | ")}\"");

                // 5. They request a movie; it's approved from /admin requests.
                var message = await gateway.SendMessageAsync(channel.Id, new InteractionReply { Content = $"🎬 **Movies request** · details (tmdb:{Matrix})" });
                var requestRun = new RecordingResponder(null, channel.Id, message, false);
                await dispatcher.DispatchAsync(FakeInteraction.Button($"req-make:movie:{Matrix}").By(target.Id).InGuild(guild.Id).InChannel(channel.Id).OnMessage(message).Build(), requestRun);
                await gateway.EditMessageAsync(channel.Id, message, requestRun.LastReply);
                Log($"[request] {requestRun.LastReply.Content.Replace("\n", " | ")}");

                var id = "1";
                var requestCard = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin requests")));
                Log($"[walk] {requestCard.LastReply.Content.Replace("\n", " | ")} \"{requestCard.LastReply.Embeds.Single().Title}\" buttons=[{string.Join("] [", requestCard.LastReply.Buttons.Select(b => b.Label))}]");
                var approve = await Send(dispatcher, AsOwner(FakeInteraction.Button(requestCard.LastReply.Buttons.Single(b => b.Label == DiscordText.ApproveLabel).CustomId)));
                Log($"[approve] the walk now: {approve.LastReply.Content.Replace("\n", " | ")}");

                var request = Mocker.Resolve<IRequestRepository>().Get(int.Parse(id));
                var reposted = await channel.GetMessageAsync(ulong.Parse(request.MessageId));
                Log($"[discord] request #{request.Id} status={request.Status}; the user's message, read back: \"{reposted.Content.Replace("\n", " | ")}\"");
                request.Status.Should().Be(RequestStatus.Approved);
                reposted.Content.Should().StartWith($"<@{target.Id}> {DiscordText.FinalApproved}");

                var requests = Mocker.Resolve<IRequestRepository>();
                var feedChannel = ulong.Parse(guildSetup.GetConfig().FeedChannelId);
                var feedRest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(feedChannel);
                var approvalCard = await adminRest.GetMessageAsync(ulong.Parse(request.AdminMessageId));
                Log($"[approval card] #1 read back: \"{approvalCard.Content.Replace("\n", " | ")}\" buttons={Buttons(approvalCard).Count}");
                approvalCard.Content.Should().StartWith("**Request #1: approved**");

                // 6. Deny from the approval card (old /admin deny): the button opens the reason form, submitted from that card.
                var heat = await RequestInRealMessage(dispatcher, gateway, guild.Id, channel.Id, target.Id, 949);
                var heatRequest = requests.All().Single(r => r.TmdbId == 949);
                var heatCard = ulong.Parse(heatRequest.AdminMessageId);
                var denyForm = await Send(dispatcher, AsOwner(FakeInteraction.Button($"req-deny:{heatRequest.Id}")).OnMessage(heatCard));
                Log($"[deny] approval card button opened the form {denyForm.Responder.LastModal?.CustomId}");
                await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit($"req-deny-reason:{heatRequest.Id}").WithField(RequestRoutes.ReasonField, "not this one")).OnMessage(heatCard));
                var heatCardNow = await adminRest.GetMessageAsync(heatCard);
                var heatMessage = await channel.GetMessageAsync(ulong.Parse(requests.Get(heatRequest.Id).MessageId));
                Log($"[approval card] #{heatRequest.Id} read back: \"{heatCardNow.Content.Replace("\n", " | ")}\" buttons={Buttons(heatCardNow).Count}; user message: \"{heatMessage.Content.Replace("\n", " | ")}\"");
                requests.Get(heatRequest.Id).Status.Should().Be(RequestStatus.Denied);
                heatCardNow.Content.Should().StartWith($"**Request #{heatRequest.Id}: denied**");
                Buttons(heatCardNow).Should().BeEmpty();
                _ = heat;

                // 7. Cancel (old /admin cancel, with a reason) and Already available (old /admin available) from feed cards.
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channel.Id, target.Id, 27205);
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channel.Id, target.Id, 157336);
                var inception = requests.All().Single(r => r.TmdbId == 27205);
                var interstellar = requests.All().Single(r => r.TmdbId == 157336);

                FakeInteraction InFeed(FakeInteraction i, Request r) => i.By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(feedChannel).OnMessage(ulong.Parse(requests.Get(r.Id).FeedMessageId));
                var cancelForm = await Send(dispatcher, InFeed(FakeInteraction.Button($"req-admin-cancel:{inception.Id}"), inception));
                Log($"[feed] Cancel on #{inception.Id}'s card opened the form {cancelForm.Responder.LastModal?.CustomId}");
                var cancelled = await Send(dispatcher, InFeed(FakeInteraction.ModalSubmit($"req-cancel-reason:{inception.Id}").WithField(RequestRoutes.ReasonField, "duplicate"), inception));
                Log($"[feed] private reply: {cancelled.LastReply.Content}");
                var available = await Send(dispatcher, InFeed(FakeInteraction.Button($"req-available:{interstellar.Id}"), interstellar));
                Log($"[feed] private reply: {available.LastReply.Content}");

                foreach (var r in new[] { requests.Get(1), requests.Get(heatRequest.Id), requests.Get(inception.Id), requests.Get(interstellar.Id) })
                {
                    var feedCard = await feedRest.GetMessageAsync(ulong.Parse(r.FeedMessageId));
                    Log($"[feed card] #{r.Id} {r.Label} status={r.Status}: \"{feedCard.Embeds.First().Description.Replace("\n", " | ")}\" buttons=[{string.Join("] [", Buttons(feedCard))}]");
                }

                requests.Get(inception.Id).Status.Should().Be(RequestStatus.Cancelled);
                requests.Get(inception.Id).DecisionReason.Should().Be("duplicate");
                requests.Get(interstellar.Id).Status.Should().Be(RequestStatus.Available);
                (await feedRest.GetMessageAsync(ulong.Parse(requests.Get(inception.Id).FeedMessageId))).Embeds.First().Description.Should().StartWith(DiscordText.FinalCancelled);

                // 8. A Plex link the member asked for (old /admin mediaserver approve): the access-request card's Approve.
                var userPanel2 = await channel.GetMessageAsync(ulong.Parse(Mocker.Resolve<IRequestarrUserRepository>().Get(user.Id).PanelMessageId));
                Log($"[plex] their panel buttons before: [{string.Join("] [", Buttons(userPanel2))}]");
                await Send(dispatcher, FakeInteraction.ModalSubmit(PanelRoutes.LinkPlexForm).WithField(PanelRoutes.PlexUsernameField, "live_test_plex").By(target.Id).InGuild(guild.Id).InChannel(channel.Id));
                var accessRequest = Mocker.Resolve<IMediaServerAccessRequestRepository>().All().Single();
                var accessCard = (await adminRest.GetMessagesAsync(10).FlattenAsync()).First(m => m.Content.StartsWith("**Media server access request**"));
                Log($"[plex] access-request card read back: \"{accessCard.Content.Replace("\n", " | ")}\" buttons=[{string.Join("] [", Buttons(accessCard))}]");
                var linkCard = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin user").WithOption("member", target.Id)));
                Log($"[plex] member card while waiting: buttons=[{string.Join("] [", linkCard.LastReply.Buttons.Select(b => b.Label))}]");
                var approved = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{MediaServerRoutes.Approve}:{accessRequest.Id}")).OnMessage(accessCard.Id));
                Log($"[plex] Approve on the access-request card: {approved.LastReply.Content.Replace("\n", " | ")}");
                var panelAfter = await channel.GetMessageAsync(ulong.Parse(Mocker.Resolve<IRequestarrUserRepository>().Get(user.Id).PanelMessageId));
                Log($"[plex] their panel buttons after: [{string.Join("] [", Buttons(panelAfter))}]; linked account: {Mocker.Resolve<IMediaServerAccountRepository>().GetForUser(user.Id).Single().Username}");
                Buttons(panelAfter).Should().NotContain(DiscordText.PanelLinkPlex);

                // 9. Old /admin user mediaserver: the member card's Link Plex form (here: change the link).
                var plexForm = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Plex}:{target.Id}")));
                Log($"[plex form] {DiscordText.ChangePlexLabel} opened {plexForm.Responder.LastModal?.CustomId}");
                var relinked = await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit($"{AdminRoutes.PlexForm}:{target.Id}").WithField(AdminRoutes.PlexField, "live_test_plex2")));
                Log($"[plex form] {relinked.LastReply.Content}; linked account now: {Mocker.Resolve<IMediaServerAccountRepository>().GetForUser(user.Id).Single().Username}");
                Mocker.Resolve<IMediaServerAccountRepository>().GetForUser(user.Id).Single().Username.Should().Be("live_test_plex2");

                // 10. Old /admin queue [user]: the panel's Pending queue (now the walk) and the card's "Their requests".
                await RequestInRealMessage(dispatcher, gateway, guild.Id, channel.Id, target.Id, 438631);
                var queue = await Send(dispatcher, AsOwner(FakeInteraction.Button(PanelRoutes.AdminQueue)));
                Log($"[queue] {queue.LastReply.Content.Replace("\n", " | ")} buttons=[{string.Join("] [", queue.LastReply.Buttons.Select(b => b.Label))}]");
                var theirs = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.More}:{target.Id}", "requests")));
                Log($"[queue] Their requests: {theirs.LastReply.Content.Replace("\n", " | ")}");
                queue.LastReply.Content.Should().Contain("Dune (2021)");
                theirs.LastReply.SelectMenu.Options.Should().HaveCount(requests.All().Count());

                // 11. Old /admin user suspend / unsuspend: the card's buttons; the real channel's lock read back.
                bool CanPost(global::Discord.Rest.RestTextChannel c) => c.PermissionOverwrites.Single(o => o.TargetId == role.Id).Permissions.SendMessages != PermValue.Deny;
                var suspended = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Suspend}:{target.Id}")));
                var locked = (global::Discord.Rest.RestTextChannel)await host.Client.Rest.GetChannelAsync(channel.Id);
                Log($"[suspend] {suspended.LastReply.Content}; card buttons now [{string.Join("] [", suspended.Responder.Replies.First().Buttons.Select(b => b.Label))}]; channel send for their role: {(CanPost(locked) ? "allowed" : "denied")}");
                CanPost(locked).Should().BeFalse();
                var unsuspended = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Unsuspend}:{target.Id}")));
                var unlocked = (global::Discord.Rest.RestTextChannel)await host.Client.Rest.GetChannelAsync(channel.Id);
                Log($"[unsuspend] {unsuspended.LastReply.Content}; channel send for their role: {(CanPost(unlocked) ? "allowed" : "denied")}");
                CanPost(unlocked).Should().BeTrue();

                // 12. Old /admin user info: the card itself, read once more before removal.
                var info = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin user").WithOption("member", target.Id)));
                Log($"[info] {info.LastReply.Content.Replace("\n", " | ")}");
                info.LastReply.Content.Should().Contain("Status: active").And.Contain("Plex `live_test_plex2`");

                // 13. Old /admin user remove: Remove asks first, Yes removes; the real channel and role read back.
                var ask = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Remove}:{target.Id}")));
                Log($"[remove] asked: buttons [{string.Join("] [", ask.LastReply.Buttons.Select(b => b.Label))}]");
                var removed = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.RemoveConfirm}:{target.Id}")));
                var archived = (global::Discord.Rest.RestTextChannel)await host.Client.Rest.GetChannelAsync(channel.Id);
                var roleGone = (await host.Client.Rest.GetGuildAsync(guild.Id)).Roles.All(r => r.Id != role.Id);
                Log($"[remove] {removed.LastReply.Content}; channel now #{archived.Name}; role deleted: {roleGone}; card now offers [{string.Join("] [", removed.Responder.Replies.First().Buttons.Select(b => b.Label))}]");
                archived.Name.Should().StartWith(ChannelLayout.ArchivedPrefix);
                roleGone.Should().BeTrue();
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

        private static List<string> Buttons(IMessage message) =>
            message.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => b.Label).ToList();

        // Posts the request's message as the bot (as /request leaves it), then presses Request on it.
        private static async Task<ulong> RequestInRealMessage(InteractionDispatcher dispatcher, IDiscordGuildGateway gateway, ulong guild, ulong channel, ulong user, int tmdbId)
        {
            var message = await gateway.SendMessageAsync(channel, new InteractionReply { Content = $"🎬 **Movies request** · details (tmdb:{tmdbId})" });
            var responder = new RecordingResponder(null, channel, message, false);
            await dispatcher.DispatchAsync(FakeInteraction.Button($"req-make:movie:{tmdbId}").By(user).InGuild(guild).InChannel(channel).OnMessage(message).Build(), responder);
            await gateway.EditMessageAsync(channel, message, responder.LastReply);
            Log($"[request] tmdb:{tmdbId} -> {responder.LastReply.Content.Replace("\n", " | ")}");
            return message;
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
