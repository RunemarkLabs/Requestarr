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
using Requestarr.Core.Audit;
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

namespace Requestarr.Core.Test.Gaps
{
    // Live: the real bot in the Discord test server and the dev stack's TEST Radarr. A request
    // made in a real message is approved (and another denied) from the admin side; the edited
    // messages are read back from Discord. Never runs in CI (ManualTest). Needs the dev stack's
    // Requestarr stopped and a clean server (docker/dev.sh reset-uat --stopped).
    [TestFixture]
    [ManualTest]
    public class LiveRequestMessageFixture : DbTest
    {
        private const int Matrix = 603;
        private const int Heat = 949;
        private const int Inception = 27205;
        private const int Interstellar = 157336;
        private const int Arrival = 329865;

        [Test]
        public async Task decided_requests_edit_their_real_messages()
        {
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            var radarrKey = Environment.GetEnvironmentVariable("RADARR_TEST_API_KEY");

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(radarrKey))
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
                Radarr = new List<BackendInstanceProvisioning>
                {
                    new() { Name = "Radarr (test)", Url = $"http://localhost:{Environment.GetEnvironmentVariable("RADARR_TEST_PORT") ?? "17878"}", ApiKey = radarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true }
                }
            });
            var tmdb = new TmdbSettingsStore();
            tmdb.Set(Environment.GetEnvironmentVariable("TMDB_API_KEY"));
            Mocker.SetConstant<ITmdbSettingsStore>(tmdb);
            Mocker.SetConstant<ITmdbClient>(new TmdbClient(tmdb, LogManager.GetLogger("LiveTmdb")));
            Mocker.SetConstant<IMetadataService>(Mocker.Resolve<MetadataService>());

            IGuildSetupService guildSetup = null;
            IUserManagementService users = null;
            IPanelService panels = null;
            Requestarr.Core.Requests.IRequestFeed feed = null;
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => users),
                new Lazy<IPanelService>(() => panels),
                new Lazy<Requestarr.Core.Requests.IRequestFeed>(() => feed),
                Mocker.Resolve<IEventAggregator>(),
                LogManager.GetLogger("LiveDiscord"));

            var gateway = new DiscordNetGateway(host, settings);
            Mocker.SetConstant<IDiscordClientHost>(host);
            Mocker.SetConstant<IDiscordGuildGateway>(gateway);
            guildSetup = Mocker.Resolve<GuildSetupService>();
            feed = Mocker.Resolve<Requestarr.Core.Requests.RequestFeed>();
            Mocker.SetConstant(guildSetup);
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            var handlers = new IInteractionHandler[] { Mocker.Resolve<UserCommandHandler>(), Mocker.Resolve<RequestHandler>(), Mocker.Resolve<ApprovalHandler>() };
            var dispatcher = new InteractionDispatcher(handlers, LogManager.GetLogger("LiveDiscord"));

            try
            {
                await host.StartAsync(settings.Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

                var guild = host.Client.GetGuild(settings.Current.GuildId);
                var adminChannel = ulong.Parse(guildSetup.GetConfig().AdminChannelId);
                var members = await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync();
                var target = members.First(m => !m.IsBot && m.Id != guild.OwnerId);

                await Send(dispatcher, FakeInteraction.SlashCommand("admin user add").WithOption("member", target.Id).By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel));
                var channel = ulong.Parse(users.FindByDiscordId(target.Id).PrivateChannelId);
                Log($"[setup] {target.Username} added; request channel {channel}");

                // Two requests, each in its own real message, as the user's /request would leave them.
                var approved = await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, target.Id, Matrix);
                var denied = await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, target.Id, Heat);
                var inLibrary = await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, target.Id, Inception);
                var userCancelled = await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, target.Id, Interstellar);
                var onDisc = await RequestInRealMessage(dispatcher, gateway, guild.Id, channel, target.Id, Arrival);

                // Inception lands in the library while its request waits (added to the test Radarr directly).
                var radarr = Mocker.Resolve<IRadarrApi>();
                var radarrSettings = Mocker.Resolve<IBackendService>().Default(ContentType.Movie).Settings;
                var profile = BackendService.ResolveProfile(await radarr.GetQualityProfilesAsync(radarrSettings), "HD-1080p");
                await radarr.AddMovieAsync(radarrSettings, await radarr.LookupTmdbAsync(radarrSettings, Inception), profile.Id, "/data/movies");
                Log("[library] Inception added to the test Radarr while request #3 was pending");

                await Send(dispatcher, FakeInteraction.Button("req-approve:1").By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel));
                await Send(dispatcher, FakeInteraction.ModalSubmit("req-deny-reason:2").WithField(RequestRoutes.ReasonField, "not something this server collects").By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel));
                var cancelRun = await Send(dispatcher, FakeInteraction.SlashCommand("admin cancel").WithOption("id", 3L).WithOption("reason", "we already have it").By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel));
                Log($"[in library] admin closed request #3 with /admin cancel; admin reply: {cancelRun.LastReply.Content}");
                var discRun = await Send(dispatcher, FakeInteraction.Button("req-available:5").By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel));
                Log($"[on disc] admin pressed Already available on request #5; card now: {discRun.Responder.Replies.First().Content}");
                var userCancel = await Send(dispatcher, FakeInteraction.Button("req-cancel:4").By(target.Id).InGuild(guild.Id).InChannel(channel).OnMessage(userCancelled));
                Log($"[user cancel] reply on the clicked message: {userCancel.LastReply.Content.Replace("\n", " | ")}");

                var userChannel = (global::Discord.IMessageChannel)await host.Client.Rest.GetChannelAsync(channel);
                var admin = (global::Discord.IMessageChannel)await host.Client.Rest.GetChannelAsync(adminChannel);
                var requests = Mocker.Resolve<IRequestRepository>();

                foreach (var (label, messageId, request) in new[] { ("approved", approved, requests.Get(1)), ("denied", denied, requests.Get(2)), ("in library", inLibrary, requests.Get(3)), ("user cancel", userCancelled, requests.Get(4)), ("on disc", onDisc, requests.Get(5)) })
                {
                    var message = await userChannel.GetMessageAsync(messageId);
                    var card = await admin.GetMessageAsync(ulong.Parse(request.AdminMessageId));
                    var buttons = message.Components.OfType<global::Discord.ActionRowComponent>().SelectMany(r => r.Components).Count();
                    var cardButtons = card.Components.OfType<global::Discord.ActionRowComponent>().SelectMany(r => r.Components).Count();

                    Log($"[{label}] request #{request.Id} status={request.Status}; stored message {request.MessageId} in channel {request.MessageChannelId}");
                    Log($"[{label}] user message read back: \"{message.Content.Replace("\n", " | ")}\" buttons={buttons} embed=\"{message.Embeds.FirstOrDefault()?.Title}\" colour={message.Embeds.FirstOrDefault()?.Color}");
                    Log($"[{label}] admin card read back: \"{card.Content.Replace("\n", " | ")}\" buttons={cardButtons}");

                    buttons.Should().Be(0);
                    cardButtons.Should().Be(0);
                }

                (await userChannel.GetMessageAsync(approved)).Content.Should().StartWith(DiscordText.FinalApproved);
                (await userChannel.GetMessageAsync(denied)).Content.Should().StartWith(DiscordText.FinalDenied).And.Contain("not something this server collects");

                // Already there is a win for the user: green and good news, whatever the admin pressed.
                foreach (var (messageId, label) in new[] { (inLibrary, "Inception (2010)"), (onDisc, "Arrival (2016)") })
                {
                    var win = await userChannel.GetMessageAsync(messageId);
                    win.Content.Should().StartWith(DiscordText.FinalAvailable).And.Contain($"Good news: {label} is already in the library");
                    win.Content.Should().NotContainAny("⛔", "🚫", "⚠️", "Cancelled", "Denied");
                    win.Embeds.Single().Color.Value.RawValue.Should().Be(RequestCards.SentColor);
                }

                (await userChannel.GetMessageAsync(userCancelled)).Content.Should().StartWith(DiscordText.FinalCancelled).And.Contain("Request #4 for Interstellar (2014) was cancelled.");
                (await admin.GetMessageAsync(ulong.Parse(requests.Get(3).AdminMessageId))).Content.Should().StartWith("**Request #3: already available**");
                (await admin.GetMessageAsync(ulong.Parse(requests.Get(5).AdminMessageId))).Content.Should().StartWith("**Request #5: already available**");
                (await admin.GetMessageAsync(ulong.Parse(requests.Get(4).AdminMessageId))).Content.Should().StartWith("**Request #4: cancelled** by OneRadChad");

                var mine = new[] { approved, denied, inLibrary, userCancelled, onDisc };
                var notices = (await userChannel.GetMessagesAsync(10).FlattenAsync()).Where(m => m.Author.IsBot && !mine.Contains(m.Id)).Select(m => m.Content).ToList();
                var repeats = notices.Where(n => n.StartsWith("Your request") || n.StartsWith("🎉 Good news!")).ToList();
                Log($"[notices] separate notices repeating an edited request message: {repeats.Count}");
                repeats.Should().BeEmpty("the edited request message already tells the user");

                // A late Cancel on the approved message changes nothing.
                var late = await Send(dispatcher, FakeInteraction.Button("req-cancel:1").By(target.Id).InGuild(guild.Id).InChannel(channel).OnMessage(approved));
                Log($"[late cancel] private reply: {late.LastReply.Content}; request #1 still {requests.Get(1).Status}");
                requests.Get(1).Status.Should().Be(RequestStatus.Approved);
            }
            finally
            {
                if (host.Client?.GetGuild(settings.Current.GuildId) is { } cleanupGuild)
                {
                    await cleanupGuild.BulkOverwriteApplicationCommandAsync(Array.Empty<global::Discord.ApplicationCommandProperties>());
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.sh reset-uat to clean the server)");
            }
        }

        // Posts the request's message as the bot (as /request leaves it), then presses Request on it.
        private async Task<ulong> RequestInRealMessage(InteractionDispatcher dispatcher, IDiscordGuildGateway gateway, ulong guild, ulong channel, ulong user, int tmdbId)
        {
            var message = await gateway.SendMessageAsync(channel, new InteractionReply { Content = $"🎬 **Movies request** · details (tmdb:{tmdbId})" });
            var responder = new RecordingResponder(null, channel, message, false);
            await dispatcher.DispatchAsync(FakeInteraction.Button($"req-make:movie:{tmdbId}").By(user).InGuild(guild).InChannel(channel).OnMessage(message).Build(), responder);

            // Discord would apply the handler's edit to the message; do the same through the bot.
            await gateway.EditMessageAsync(channel, message, responder.LastReply);
            Log($"[request] tmdb:{tmdbId} -> {responder.LastReply.Content.Replace("\n", " | ")}");

            return message;
        }

        private static async Task<InteractionRun> Send(InteractionDispatcher dispatcher, FakeInteraction interaction)
        {
            var responder = new RecordingResponder(null, interaction.Build().ChannelId, interaction.Build().MessageId, false);
            var outcome = await dispatcher.DispatchAsync(interaction.Build(), responder);
            return new InteractionRun(outcome, responder);
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
