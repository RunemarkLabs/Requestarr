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
using Requestarr.Core.Access;
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

namespace Requestarr.Core.Test.Welcome
{
    // Live, task 4e: the real bot in the dev server. Reads back both channel groups and their permissions, the welcome
    // channel (read-only for a non-admin, no media server buttons), and the blocked channel. As the test member: asks
    // for access, is denied, is refused within the cooldown, asks again after it and is approved (their channel
    // appears); then clicks until blocked (the blocked channel shows, the welcome channel hides), is unblocked by the
    // button and by removing the role by hand. Also: the admin panel moves below a new post, a pin leaves no "pinned a
    // message" notice, and version cards with Read changes. Clicks are simulated through the real handlers; the effects
    // are read back from Discord. Needs the dev stack's Requestarr stopped: docker/dev.sh reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveWelcomeFixture : DbTest
    {
        [Test]
        public async Task groups_welcome_access_requests_blocks_and_panels_in_the_dev_server()
        {
            var token = LiveDev.Token;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(LiveDev.RadarrKey))
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
            Mocker.SetConstant<NzbDrone.Core.Configuration.IConfigRepository>(Mocker.Resolve<NzbDrone.Core.Configuration.ConfigRepository>());
            Mocker.SetConstant<NzbDrone.Core.Configuration.IConfigService>(Mocker.Resolve<NzbDrone.Core.Configuration.ConfigService>());
            Mocker.SetConstant<IRadarrApi>(Mocker.Resolve<RadarrApi>());
            Mocker.SetConstant<ISonarrApi>(Mocker.Resolve<SonarrApi>());
            Mocker.SetConstant<IBackendService>(Mocker.Resolve<BackendService>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr (test)", Url = LiveDev.RadarrUrl, ApiKey = LiveDev.RadarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true } }
            });
            Mocker.Resolve<IBackendService>().CheckAllAsync().GetAwaiter().GetResult();
            Mocker.SetConstant<ITmdbSettingsStore>(new TmdbSettingsStore());
            Mocker.SetConstant<ITmdbClient>(new TmdbClient(new TmdbSettingsStore(), LogManager.GetLogger("LiveTmdb")));
            Mocker.SetConstant<IMetadataService>(Mocker.Resolve<MetadataService>());
            Mocker.SetConstant<IAvailabilitySettingsStore>(new AvailabilitySettingsStore());
            Mocker.SetConstant<IMediaServerConnections>(new MediaServerConnections());
            Mocker.SetConstant<IOutageRepository>(Mocker.Resolve<OutageRepository>());
            Mocker.SetConstant<IAccessRequestRepository>(Mocker.Resolve<AccessRequestRepository>());
            Mocker.SetConstant<IWelcomeGuardRepository>(Mocker.Resolve<WelcomeGuardRepository>());
            Mocker.SetConstant<IAccessSettings>(Mocker.Resolve<AccessSettings>());
            Mocker.SetConstant<IReleaseEnvironment>(new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.EnvironmentVariable ? "dev" : null });

            IGuildSetupService guildSetup = null;
            IUserManagementService users = null;
            IPanelService panels = null;
            IRequestFeed feed = null;
            AccessRequestService access = null;
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => users),
                new Lazy<IPanelService>(() => panels),
                new Lazy<IRequestFeed>(() => feed),
                new Lazy<IAccessRequestService>(() => access),
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
                Mocker.Resolve<Requestarr.Core.Admin.AccessRequestSource>()
            }));
            Mocker.SetConstant<IConnectionMonitor>(Mocker.Resolve<ConnectionMonitor>());
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            access = Mocker.Resolve<AccessRequestService>();
            Mocker.SetConstant<IAccessRequestService>(access);
            var announcements = Mocker.Resolve<AnnouncementService>();
            Mocker.SetConstant<IAnnouncementService>(announcements);
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            var handlers = new IInteractionHandler[] { Mocker.Resolve<WelcomeHandler>(), Mocker.Resolve<AccessRequestHandler>(), Mocker.Resolve<AdminConsoleHandler>(), Mocker.Resolve<AdminRequestsHandler>(), Mocker.Resolve<VersionCardHandler>() };
            var dispatcher = new InteractionDispatcher(handlers, LogManager.GetLogger("LiveDiscord"));

            try
            {
                await host.StartAsync(settings.Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

                var guild = host.Client.GetGuild(settings.Current.GuildId);
                var config = guildSetup.GetConfig();
                var adminChannel = ulong.Parse(config.AdminChannelId);
                var welcomeChannel = ulong.Parse(config.WelcomeChannelId);
                var blockedChannel = ulong.Parse(config.BlockedChannelId);
                var blockedRole = ulong.Parse(config.BlockedRoleId);
                var rest = await host.Client.Rest.GetGuildAsync(guild.Id);
                FakeInteraction AsOwner(FakeInteraction i) => i.By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel);

                // 1. Both groups, read back from Discord.
                var channels = await rest.GetChannelsAsync();
                var adminGroup = channels.OfType<ICategoryChannel>().Single(c => c.Name == ChannelLayout.AdminCategoryName);
                var usersGroup = channels.OfType<ICategoryChannel>().Single(c => c.Name == ChannelLayout.UsersCategoryName);
                string Overwrites(IGuildChannel c) => string.Join("; ", c.PermissionOverwrites.Select(o => $"{Name(o.TargetId)} allow[{string.Join(",", o.Permissions.ToAllowList())}] deny[{string.Join(",", o.Permissions.ToDenyList())}]"));
                string Name(ulong id) => id == guild.Id ? "@everyone" : rest.GetRole(id)?.Name ?? (id == host.Client.CurrentUser.Id ? "the bot" : id.ToString());
                INestedChannel Text(string name) => channels.OfType<INestedChannel>().Single(c => c.Name == name);

                Log($"[groups] {adminGroup.Name}: {Overwrites(adminGroup)}");
                foreach (var name in new[] { ChannelLayout.AdminChannelName, ChannelLayout.FeedChannelName })
                {
                    var channel = Text(name);
                    var synced = channel.PermissionOverwrites.Count == adminGroup.PermissionOverwrites.Count && channel.PermissionOverwrites.All(o => adminGroup.PermissionOverwrites.Contains(o));
                    Log($"[groups]   #{name}: in {(channel.CategoryId == adminGroup.Id ? adminGroup.Name : "another group")}, follows the group's permissions: {synced}");
                    channel.CategoryId.Should().Be(adminGroup.Id);
                    synced.Should().BeTrue();
                }

                adminGroup.PermissionOverwrites.Single(o => o.TargetId == guild.Id).Permissions.ViewChannel.Should().Be(PermValue.Deny);
                Log($"[groups] {usersGroup.Name}: {Overwrites(usersGroup)}");
                foreach (var name in new[] { ChannelLayout.WelcomeChannelName, ChannelLayout.AnnouncementsChannelName, ChannelLayout.BlockedChannelName })
                {
                    Log($"[groups]   #{name}: in {(Text(name).CategoryId == usersGroup.Id ? usersGroup.Name : "another group")}; {Overwrites(Text(name))}");
                    Text(name).CategoryId.Should().Be(usersGroup.Id);
                }

                channels.OfType<ICategoryChannel>().Should().NotContain(c => c.Name.StartsWith(ChannelLayout.OldRequestCategoryName));

                // 2. The welcome channel as the test member sees it, and its message.
                var member = LiveTestMembers.Pick(await rest.GetUsersAsync().FlattenAsync())[0];
                await guild.DownloadUsersAsync();
                global::Discord.ChannelPermissions Perms(ulong channelId) => guild.GetUser(member.Id).GetPermissions(guild.GetChannel(channelId));
                bool Sees(ulong channelId) => Perms(channelId).ViewChannel;
                var welcomePerms = Perms(welcomeChannel);
                Log($"[welcome] {member.Username} in #{ChannelLayout.WelcomeChannelName}: view {welcomePerms.ViewChannel}, read history {welcomePerms.ReadMessageHistory}, send {welcomePerms.SendMessages}, start threads {welcomePerms.CreatePublicThreads}; sees #{ChannelLayout.BlockedChannelName}: {Sees(blockedChannel)}");
                (welcomePerms.ViewChannel, welcomePerms.SendMessages, welcomePerms.CreatePublicThreads, welcomePerms.CreatePrivateThreads).Should().Be((true, false, false, false));
                Sees(blockedChannel).Should().BeFalse();

                var welcomeRest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(welcomeChannel);
                var welcome = await welcomeRest.GetMessageAsync(ulong.Parse(guildSetup.GetConfig().WelcomeMessageId));
                Log($"[welcome] message: \"{welcome.Content.Replace("\n", " | ")}\"");
                Log($"[welcome] buttons: [{string.Join("] [", Buttons(welcome))}] (no media server is connected)");
                Buttons(welcome).Should().Equal(DiscordText.RequestAccessLabel);

                // 3. An access request, denied; refused within the cooldown.
                Task<InteractionRun> Press(string customId = WelcomeRoutes.Request) =>
                    Send(dispatcher, FakeInteraction.Button(customId).By(member.Id, member.Username).InGuild(guild.Id).InChannel(welcomeChannel));

                async Task<InteractionRun> Ask(string note)
                {
                    var form = await Press();
                    Log($"[request] form: \"{form.LastReply.Content.Replace("\n", " | ")}\" options [{string.Join("] [", form.LastReply.SelectMenu.Options.Select(o => o.Label))}]");
                    var picked = await Send(dispatcher, FakeInteraction.SelectMenu(WelcomeRoutes.Types, "movie").By(member.Id).InGuild(guild.Id).InChannel(welcomeChannel));
                    var next = await Send(dispatcher, FakeInteraction.Button(picked.LastReply.Buttons.Single().CustomId).By(member.Id).InGuild(guild.Id).InChannel(welcomeChannel));
                    return await Send(dispatcher, FakeInteraction.ModalSubmit(next.Responder.LastModal.CustomId).WithField(WelcomeRoutes.NoteField, note).By(member.Id, member.Username).InGuild(guild.Id).InChannel(welcomeChannel));
                }

                var sent = await Ask("Live test: please let me in");
                Log($"[request] {member.Username}: \"{sent.LastReply.Content}\"");
                var adminRest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(adminChannel);
                var card = (await adminRest.GetMessagesAsync(10).FlattenAsync()).First(m => m.Content.StartsWith("🙋"));
                Log($"[request] card in #{ChannelLayout.AdminChannelName}: \"{card.Content.Replace("\n", " | ")}\" buttons [{string.Join("] [", Buttons(card))}]");
                var walk = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin requests")));
                Log($"[request] /admin requests: \"{walk.LastReply.Content.Replace("\n", " | ")}\" buttons [{string.Join("] [", walk.LastReply.Buttons.Select(b => b.Label))}]");
                walk.LastReply.Content.Should().Contain("Access request");

                await Task.Delay(TimeSpan.FromSeconds(4));
                var newest = (await adminRest.GetMessagesAsync(1).FlattenAsync()).Single();
                Log($"[panels] newest message in #{ChannelLayout.AdminChannelName} after the card: {(newest.Id.ToString() == guildSetup.GetConfig().AdminPanelMessageId ? "the admin panel" : "something else")} (pinned: {newest.IsPinned})");
                newest.Id.ToString().Should().Be(guildSetup.GetConfig().AdminPanelMessageId, "the panel moved below the card");

                var request = Mocker.Resolve<IAccessRequestRepository>().All().Single();
                await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AccessRoutes.Deny}:{request.Id}")));
                var denied = await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit($"{AccessRoutes.DenyForm}:{request.Id}").WithField(AccessRoutes.ReasonField, "Live test denial")));
                Log($"[deny] {denied.LastReply.Content}; {member.Username} was sent a private message with the reason");
                var refused = await Press();
                Log($"[cooldown] pressing again: \"{refused.LastReply.Content}\"");
                refused.LastReply.Content.Should().StartWith("Your last request was declined: Live test denial").And.Contain("You can ask again");
                refused.LastReply.SelectMenu.Should().BeNull();

                // 4. After the cooldown (the denial is dated back past it; the clock isn't moved, so cards say "asked
                // just now"): asked again and approved; their channel appears, and the approval names the live channel.
                request = Mocker.Resolve<IAccessRequestRepository>().Get(request.Id);
                request.Decided = DateTime.UtcNow.AddDays(-AccessSettings.DefaultDenialCooldownDays - 1);
                Mocker.Resolve<IAccessRequestRepository>().Update(request);
                await Ask("Live test: second try");
                var second = Mocker.Resolve<IAccessRequestRepository>().All().OrderBy(r => r.Id).Last();
                var chooser = (await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AccessRoutes.Approve}:{second.Id}")))).LastReply;
                Log($"[approve] chooser: \"{chooser.Content.Replace("\n", " | ")}\"");
                var approved = await Send(dispatcher, AsOwner(FakeInteraction.Button(chooser.Buttons.Single().CustomId)));
                Log($"[approve] {approved.LastReply.Content}");
                var user = users.FindByDiscordId(member.Id);
                var theirChannel = (ITextChannel)await host.Client.Rest.GetChannelAsync(ulong.Parse(user.PrivateChannelId));
                Log($"[approve] #{theirChannel.Name} exists, in {(theirChannel.CategoryId == usersGroup.Id ? usersGroup.Name : "another group")}; the approval names <#{theirChannel.Id}>: {approved.LastReply.Content.Contains($"<#{theirChannel.Id}>")}");
                theirChannel.CategoryId.Should().Be(usersGroup.Id);
                theirChannel.Name.Should().Be(ChannelLayout.UserChannelName(member.Username), "the live channel, not an archived one");
                approved.LastReply.Content.Should().Contain($"<#{theirChannel.Id}>");
                var secondCard = (await adminRest.GetMessagesAsync(20).FlattenAsync()).First(m => m.Content.StartsWith("🙋") && m.Content.Contains("second try"));
                Log($"[approve] second card: \"{secondCard.Content.Replace("\n", " | ")}\"");
                (await Press()).LastReply.Content.Should().Be(DiscordText.WelcomeAlreadyIn(user.PrivateChannelId));

                // 5. Repeated clicks: the long wait, then blocked; read back from Discord; Unblock restores.
                async Task ClickUntilBlocked()
                {
                    for (var i = 1; i <= 15; i++)
                    {
                        var answer = (await Press()).LastReply.Content;

                        if (i is 5 or 10 or 15)
                        {
                            Log($"[spam] click {i}: \"{answer.Replace("\n", " | ")}\"");
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2));
                    await guild.DownloadUsersAsync();
                }

                await ClickUntilBlocked();
                var blockedMember = await host.Client.Rest.GetGuildUserAsync(guild.Id, member.Id);
                Log($"[block] {member.Username} has {ChannelLayout.BlockedRoleName}: {blockedMember.RoleIds.Contains(blockedRole)}; sees #{ChannelLayout.WelcomeChannelName}: {Sees(welcomeChannel)}; sees #{ChannelLayout.BlockedChannelName}: {Sees(blockedChannel)}");
                blockedMember.RoleIds.Should().Contain(blockedRole);
                Sees(welcomeChannel).Should().BeFalse();
                Sees(blockedChannel).Should().BeTrue();
                var notice = (await adminRest.GetMessagesAsync(10).FlattenAsync()).First(m => m.Content.StartsWith("🚫"));
                Log($"[block] admin notice: \"{notice.Content}\" buttons [{string.Join("] [", Buttons(notice))}]");
                var blockedMessage = await ((IMessageChannel)await host.Client.Rest.GetChannelAsync(blockedChannel)).GetMessageAsync(ulong.Parse(guildSetup.GetConfig().BlockedMessageId));
                Log($"[block] #{ChannelLayout.BlockedChannelName}: \"{blockedMessage.Content.Replace("\n", " | ")}\"");

                var card2 = (await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin user").WithOption("member", member.Id)))).LastReply;
                Log($"[block] member card buttons: [{string.Join("] [", card2.Buttons.Select(b => b.Label))}]");
                var unblocked = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AccessRoutes.Unblock}:{member.Id}")));
                await Task.Delay(TimeSpan.FromSeconds(2));
                await guild.DownloadUsersAsync();
                Log($"[unblock] {unblocked.LastReply.Content}; sees #{ChannelLayout.WelcomeChannelName}: {Sees(welcomeChannel)}; sees #{ChannelLayout.BlockedChannelName}: {Sees(blockedChannel)}");
                Sees(welcomeChannel).Should().BeTrue();
                Sees(blockedChannel).Should().BeFalse();

                // Blocked again: one notice for them, not two (the earlier one makes way); then the role removed by hand.
                await ClickUntilBlocked();
                var notices = (await adminRest.GetMessagesAsync(50).FlattenAsync()).Count(m => m.Content.StartsWith("🚫") && m.Content.Contains($"<@{member.Id}>"));
                Log($"[block] blocked a second time: block notices about {member.Username} in #{ChannelLayout.AdminChannelName}: {notices}");
                notices.Should().Be(1);
                await host.Client.Rest.RemoveRoleAsync(guild.Id, member.Id, blockedRole);
                await Task.Delay(TimeSpan.FromSeconds(4));
                Log($"[unblock] role removed by hand: Requestarr counts them unblocked: {!access.IsBlocked(member.Id)}");
                access.IsBlocked(member.Id).Should().BeFalse();

                // 6. A pin leaves no "pinned a message" notice.
                var pinTest = await gateway.SendMessageAsync(adminChannel, new InteractionReply { Content = "Live test: a notice pinned and cleaned up" });
                await gateway.PinMessageAsync(adminChannel, pinTest);
                await Task.Delay(TimeSpan.FromSeconds(2));
                var recent = (await adminRest.GetMessagesAsync(10).FlattenAsync()).ToList();
                var pinNotices = recent.Count(m => m.Type == MessageType.ChannelPinnedMessage);
                Log($"[pins] pinned: {(await adminRest.GetMessageAsync(pinTest))?.IsPinned}; \"pinned a message\" notices left: {pinNotices}");
                pinNotices.Should().Be(0);
                await gateway.UnpinMessageAsync(adminChannel, pinTest);
                await gateway.DeleteMessageAsync(adminChannel, pinTest);

                // 7. Version cards, and Read changes.
                await announcements.PostVersionNotesAsync();
                var cards = (await adminRest.GetMessagesAsync(20).FlattenAsync()).Where(m => m.Embeds.FirstOrDefault()?.Title?.StartsWith("Requestarr ") == true).ToList();
                foreach (var versionCard in cards)
                {
                    Log($"[cards] #{ChannelLayout.AdminChannelName}: \"{versionCard.Embeds.First().Title}\" buttons [{string.Join("] [", Buttons(versionCard))}]");
                }

                var usersCards = (await ((IMessageChannel)await host.Client.Rest.GetChannelAsync(ulong.Parse(guildSetup.GetConfig().AnnouncementsChannelId))).GetMessagesAsync(20).FlattenAsync())
                    .Where(m => m.Embeds.Any()).ToList();
                foreach (var versionCard in usersCards)
                {
                    Log($"[cards] #{ChannelLayout.AnnouncementsChannelName}: \"{versionCard.Embeds.First().Title}\" · \"{versionCard.Embeds.First().Description}\"");
                }

                cards.Should().NotBeEmpty();
                var read = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{VersionCardRoutes.ReadChanges}:a:{Changelog.Release(NzbDrone.Common.EnvironmentInfo.BuildInfo.Version)}")));
                Log($"[cards] Read changes (private: {read.LastReply.Ephemeral}): \"{read.LastReply.Embeds.FirstOrDefault()?.Title}\", {read.LastReply.Embeds.Count} section(s)");
                read.LastReply.Ephemeral.Should().BeTrue();

                // 8. The version card sits right above the one admin panel.
                await Task.Delay(TimeSpan.FromSeconds(4));
                var latest = (await adminRest.GetMessagesAsync(3).FlattenAsync()).OrderByDescending(m => m.Id).ToList();
                string Describe(IMessage m) => m.Id.ToString() == guildSetup.GetConfig().AdminPanelMessageId ? "the panel" : $"{m.Type} by {(m.Author.Id == host.Client.CurrentUser.Id ? "the bot" : m.Author.Username)}: \"{(m.Embeds.FirstOrDefault()?.Title ?? m.Content).Split('\n')[0]}\"";
                Log($"[cards] newest in #{ChannelLayout.AdminChannelName}, newest first: {string.Join(" / ", latest.Select(Describe))}");
                latest[0].Id.ToString().Should().Be(guildSetup.GetConfig().AdminPanelMessageId);

                // 9. A restart with a new database (a reset, or every live check): the bot finds its groups, channels, panels
                // and the member's channel again instead of making new ones.
                var before = await Snapshot(host, guild.Id, member.Username);
                Mocker.Resolve<IDiscordGuildConfigRepository>().Purge();
                Mocker.Resolve<IRequestarrUserRepository>().Purge();
                Mocker.Resolve<IUserIdentityRepository>().Purge();
                Mocker.Resolve<IUserMediaAccessRepository>().Purge();
                await guildSetup.EnsureAsync();
                await panels.EnsureAllAsync();
                await users.AddAsync(member.Id, new Dictionary<ContentType, AccessState>(), Actor.System);
                await Task.Delay(TimeSpan.FromSeconds(4));
                var after = await Snapshot(host, guild.Id, member.Username);
                Log($"[restart] before: {before}");
                Log($"[restart] after a new database: {after}");
                after.Should().Be(before, "nothing duplicated, nothing lost");
                after.Should().Contain("admin panels 1").And.Contain("welcome messages 1").And.Contain("blocked messages 1").And.Contain($"#{ChannelLayout.UserChannelName(member.Username)}=1 in {ChannelLayout.UsersCategoryName}").And.Contain("their panels 1");

                // 10. A deleted users group: it comes back, and every channel Discord left loose goes back in.
                await ((IGuildChannel)await host.Client.Rest.GetChannelAsync(ulong.Parse(guildSetup.GetConfig().RequestCategoryIds[0]))).DeleteAsync();
                await Task.Delay(TimeSpan.FromSeconds(2));
                var loose = (await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetTextChannelsAsync()).Where(c => c.CategoryId == null).Select(c => "#" + c.Name).ToList();
                Log($"[groups] users group deleted; loose at the top: {string.Join(", ", loose)}");
                Mocker.Resolve<IDiscordGuildConfigRepository>().Purge();
                await guildSetup.EnsureAsync();
                var restored = await Snapshot(host, guild.Id, member.Username);
                Log($"[groups] after the next start: {restored}");
                restored.Should().Contain("loose 0").And.Contain($"#{ChannelLayout.UserChannelName(member.Username)}=1 in {ChannelLayout.UsersCategoryName}");

                // The test member stays added, with their one channel; reset-dev cleans the server afterwards.
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

        // What a server holds, read back from Discord: panels per channel, the member's channels and where they are, and
        // Requestarr channels left loose at the top.
        private static async Task<string> Snapshot(DiscordClientHost host, ulong guildId, string username)
        {
            var guild = await host.Client.Rest.GetGuildAsync(guildId);
            var channels = await guild.GetTextChannelsAsync();
            var groups = (await guild.GetCategoryChannelsAsync()).ToDictionary(c => c.Id, c => c.Name);
            var bot = host.Client.CurrentUser.Id;

            async Task<int> Count(string name, Func<IMessage, bool> what)
            {
                var channel = channels.FirstOrDefault(c => c.Name == name);
                return channel == null ? -1 : (await channel.GetMessagesAsync(100).FlattenAsync()).Count(m => m.Author.Id == bot && what(m));
            }

            bool Has(IMessage m, string route) => m.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Any(b => b.CustomId == route);
            var theirs = channels.Where(c => c.Name.EndsWith(ChannelLayout.UserChannelName(username))).ToList();
            var live = theirs.Where(c => c.Name == ChannelLayout.UserChannelName(username)).ToList();
            var requestarr = channels.Where(c => c.Name.StartsWith("requestarr-") || c.Name.Contains(ChannelLayout.UserChannelPrefix)).ToList();

            return $"admin panels {await Count(ChannelLayout.AdminChannelName, m => Has(m, PanelRoutes.AdminQueue))}, " +
                   $"welcome messages {await Count(ChannelLayout.WelcomeChannelName, m => Has(m, WelcomeRoutes.Request))}, " +
                   $"blocked messages {await Count(ChannelLayout.BlockedChannelName, m => m.Content == DiscordText.BlockedMessage)}, " +
                   $"{string.Join(", ", theirs.GroupBy(c => c.Name).Select(g => $"#{g.Key}={g.Count()} in {string.Join("/", g.Select(c => c.CategoryId.HasValue && groups.ContainsKey(c.CategoryId.Value) ? groups[c.CategoryId.Value] : "the top level"))}"))}, " +
                   $"their panels {(live.Count == 1 ? (await live[0].GetMessagesAsync(100).FlattenAsync()).Count(m => m.Author.Id == bot && Has(m, PanelRoutes.MyRequests)) : -1)}, " +
                   $"loose {requestarr.Count(c => c.CategoryId == null)}";
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
