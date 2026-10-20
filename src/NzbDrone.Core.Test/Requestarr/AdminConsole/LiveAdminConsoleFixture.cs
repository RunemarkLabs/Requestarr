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
    // access and limit on the member card; approves their request from /admin request. Clicks are
    // simulated through the real handlers (their replies are private, so they're recorded here);
    // the real effects (role, channel, panel, request message) are read back from Discord.
    // Never runs in CI (ManualTest). Needs the stack's Requestarr stopped and a clean server:
    // docker/dev.sh reset-uat --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveAdminConsoleFixture : DbTest
    {
        private const int Matrix = 603;

        private static string RadarrUrl => $"http://localhost:{Environment.GetEnvironmentVariable("RADARR_TEST_PORT") ?? "17878"}";
        private static string RadarrKey => Environment.GetEnvironmentVariable("RADARR_TEST_API_KEY");

        [Test]
        public async Task commands_member_card_and_request_card_in_the_test_server()
        {
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(RadarrKey))
            {
                Assert.Ignore("DISCORD_BOT_TOKEN / RADARR_TEST_API_KEY not set");
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
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr (test)", Url = RadarrUrl, ApiKey = RadarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true } }
            });
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
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            var handlers = new IInteractionHandler[] { Mocker.Resolve<AdminConsoleHandler>(), Mocker.Resolve<RequestHandler>(), Mocker.Resolve<ApprovalHandler>(), Mocker.Resolve<SeasonsHandler>() };
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
                registered.Single(c => c.Name == "admin").Options.Select(o => o.Name).Should().Equal("user", "request", "help");

                // 2. The admin panel's buttons, read back from the pinned panel.
                var adminRest = (IMessageChannel)await host.Client.Rest.GetChannelAsync(adminChannel);
                var panel = (await adminRest.GetPinnedMessagesAsync()).Single();
                var panelButtons = panel.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => b.Label).ToList();
                Log($"[panel] pinned admin panel buttons: [{string.Join("] [", panelButtons)}]");
                panelButtons.Should().Contain(DiscordText.PanelMembers);

                // 3. Onboard a member through the member picker.
                var members = await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync();
                var target = members.First(m => !m.IsBot && m.Id != guild.OwnerId);
                var picker = await Send(dispatcher, AsOwner(FakeInteraction.Button(AdminRoutes.Pick)));
                Log($"[picker] {picker.LastReply.Content} menu kind={picker.LastReply.SelectMenu.Kind} id={picker.LastReply.SelectMenu.CustomId}");
                var card = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu(AdminRoutes.Member, target.Id.ToString())));
                Log($"[card] {target.Username} before: \"{card.LastReply.Content.Replace("\n", " | ")}\" buttons=[{string.Join("] [", card.LastReply.Buttons.Select(b => b.Label))}]");
                var added = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{AdminRoutes.Add}:{target.Id}")));
                Log($"[add] {added.LastReply.Content.Replace("\n", " | ")}");

                var user = users.FindByDiscordId(target.Id);
                var channel = (global::Discord.Rest.RestTextChannel)await host.Client.Rest.GetChannelAsync(ulong.Parse(user.PrivateChannelId));
                var role = guild.GetRole(ulong.Parse(user.DiscordRoleId));
                Log($"[discord] channel #{channel.Name}, role {role?.Name}, pinned panel: \"{(await channel.GetPinnedMessagesAsync()).Single().Content.Replace("\n", " | ")}\"");

                // 4. Access and a limit from the member card.
                var access = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Access}:{target.Id}", "movie:approval")));
                Log($"[access] {access.LastReply.Content}");
                await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu($"{AdminRoutes.Limit}:{target.Id}", "movie")));
                var limit = await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit($"{AdminRoutes.LimitForm}:{target.Id}:movie").WithField(AdminRoutes.LimitField, "5").WithField(AdminRoutes.DaysField, "7")));
                Log($"[limit] {limit.LastReply.Content}");
                var refreshed = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin user").WithOption("member", target.Id)));
                Log($"[card] after: \"{refreshed.LastReply.Content.Replace("\n", " | ")}\"");
                refreshed.LastReply.Content.Should().Contain("Movies: needs approval (quota 5 per 7 days)");
                var userPanel = (await channel.GetPinnedMessagesAsync()).Single().Content;
                Log($"[discord] their panel now: \"{userPanel.Replace("\n", " | ")}\"");

                // 5. They request a movie; it's approved from /admin request.
                var message = await gateway.SendMessageAsync(channel.Id, new InteractionReply { Content = $"🎬 **Movies request** · details (tmdb:{Matrix})" });
                var requestRun = new RecordingResponder(null, channel.Id, message, false);
                await dispatcher.DispatchAsync(FakeInteraction.Button($"req-make:movie:{Matrix}").By(target.Id).InGuild(guild.Id).InChannel(channel.Id).OnMessage(message).Build(), requestRun);
                await gateway.EditMessageAsync(channel.Id, message, requestRun.LastReply);
                Log($"[request] {requestRun.LastReply.Content.Replace("\n", " | ")}");

                var suggestions = await Send(dispatcher, AsOwner(FakeInteraction.Autocomplete("admin request", "request", "matrix")));
                Log($"[autocomplete] \"matrix\" -> {string.Join("; ", suggestions.Responder.Suggestions.Select(s => $"{s.Name} = {s.Value}"))}");
                var id = suggestions.Responder.Suggestions[0].Value;
                var requestCard = await Send(dispatcher, AsOwner(FakeInteraction.SlashCommand("admin request").WithOption("request", id)));
                Log($"[request card] \"{requestCard.LastReply.Embeds.Single().Title}\" {requestCard.LastReply.Embeds.Single().Description.Replace("\n", " | ")} buttons=[{string.Join("] [", requestCard.LastReply.Buttons.Select(b => b.Label))}]");
                var approve = await Send(dispatcher, AsOwner(FakeInteraction.Button($"req-approve:{id}")));
                Log($"[approve] card now: \"{approve.Responder.Replies.First().Embeds.Single().Description.Replace("\n", " | ")}\"; private reply: {approve.LastReply.Content}");

                var request = Mocker.Resolve<IRequestRepository>().Get(int.Parse(id));
                var reposted = await channel.GetMessageAsync(ulong.Parse(request.MessageId));
                Log($"[discord] request #{request.Id} status={request.Status}; the user's message, read back: \"{reposted.Content.Replace("\n", " | ")}\"");
                request.Status.Should().Be(RequestStatus.Approved);
                reposted.Content.Should().StartWith($"<@{target.Id}> {DiscordText.FinalApproved}");
            }
            finally
            {
                if (host.Client?.GetGuild(settings.Current.GuildId) is { } cleanupGuild)
                {
                    await cleanupGuild.BulkOverwriteApplicationCommandAsync(Array.Empty<ApplicationCommandProperties>());
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.sh reset-uat to restore the stack)");
            }
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
