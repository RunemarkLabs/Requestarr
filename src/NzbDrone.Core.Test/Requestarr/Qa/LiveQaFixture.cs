using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Categories;
using Requestarr.Core.Access;
using Requestarr.Core.Admin;
using Requestarr.Core.Announcements;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Qa;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Qa
{
    // Live check in the dev Discord server (DEV_DISCORD_* from docker/secrets.env, the dev stack's bot stopped:
    // dev.ps1 -Stack dev reset-dev --stopped). The QA checklist is posted for the running version; the server owner (an
    // admin) passes items and fails one, which opens a linked bug report; the test member reports a problem and hits the
    // limit; then every item passes and the checklist completes. With DEV_GITHUB_ISSUES_TOKEN set, reports are filed as
    // real issues (labelled "test"). Clicks go through the real handlers; the effects are read back from Discord. The
    // checklist's state is saved after each step in the API's format (LIVE_QA_STATE_DIR) for docker/qa-gate.py.
    [TestFixture]
    [ManualTest]
    public class LiveQaFixture : DbTest
    {
        [Test]
        public async Task checklist_failing_item_bug_reports_limit_and_completion_in_the_dev_server()
        {
            if (string.IsNullOrWhiteSpace(LiveDev.Token))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN not set");
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
            Mocker.SetConstant<NzbDrone.Core.Configuration.IConfigRepository>(Mocker.Resolve<NzbDrone.Core.Configuration.ConfigRepository>());
            Mocker.SetConstant<NzbDrone.Core.Configuration.IConfigService>(Mocker.Resolve<NzbDrone.Core.Configuration.ConfigService>());
            Mocker.SetConstant<IMediaServerConnections>(new MediaServerConnections());
            Mocker.SetConstant<IAccessRequestRepository>(Mocker.Resolve<AccessRequestRepository>());
            Mocker.SetConstant<IWelcomeGuardRepository>(Mocker.Resolve<WelcomeGuardRepository>());
            Mocker.SetConstant<IReleaseEnvironment>(new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.EnvironmentVariable ? "dev" : null });
            Mocker.SetConstant<IQaChecklistItemRepository>(Mocker.Resolve<QaChecklistItemRepository>());
            Mocker.SetConstant<IBugReportRepository>(Mocker.Resolve<BugReportRepository>());
            Mocker.SetConstant<IBugReportGuardRepository>(Mocker.Resolve<BugReportGuardRepository>());

            // Filing as the dev server would with its own key; without one, reports are kept and posted only.
            var token = Environment.GetEnvironmentVariable("DEV_GITHUB_ISSUES_TOKEN");
            var bugSettings = Mocker.Resolve<BugReportSettings>();
            bugSettings.Apply(new Requestarr.Core.Provisioning.BugReportsProvisioning { FileIssues = !string.IsNullOrWhiteSpace(token), GithubToken = token, ReportsPerDay = 2, MinutesBetween = 2 });
            Mocker.SetConstant<IBugReportSettings>(bugSettings);
            Mocker.SetConstant<IGitHubIssues>(new GitHubIssues(LogManager.GetLogger("LiveGitHub")));

            IGuildSetupService guildSetup = null;
            IPanelService panels = null;
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => null),
                new Lazy<IPanelService>(() => panels),
                new Lazy<IRequestFeed>(() => null),
                new Lazy<IAccessRequestService>(() => null),
                Mocker.Resolve<IEventAggregator>(),
                LogManager.GetLogger("LiveDiscord"));

            var gateway = new DiscordNetGateway(host, settings);
            Mocker.SetConstant<IDiscordClientHost>(host);
            Mocker.SetConstant<IDiscordGuildGateway>(gateway);
            guildSetup = Mocker.Resolve<GuildSetupService>();
            Mocker.SetConstant(guildSetup);
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            Mocker.SetConstant<IAdminQueue>(new AdminQueue(Array.Empty<IAdminQueueSource>()));
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            var checklist = Mocker.Resolve<QaChecklistService>();
            Mocker.SetConstant<IQaChecklistService>(checklist);
            var reports = Mocker.Resolve<BugReportService>();
            Mocker.SetConstant<IBugReportService>(reports);
            var dispatcher = new InteractionDispatcher(new IInteractionHandler[] { Mocker.Resolve<QaHandler>() }, LogManager.GetLogger("LiveDiscord"));
            var stateDir = Environment.GetEnvironmentVariable("LIVE_QA_STATE_DIR") ?? Path.Combine(Path.GetTempPath(), "requestarr-live-qa");
            Directory.CreateDirectory(stateDir);

            await host.StartAsync(settings.Current);
            await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

            try
            {
                await checklist.EnsureAsync();
                await panels.EnsureAllAsync();

                var guild = host.Client.GetGuild(settings.Current.GuildId);
                var rest = await host.Client.Rest.GetGuildAsync(guild.Id);
                var config = guildSetup.GetConfig();
                var checklistChannel = ulong.Parse(config.QaChecklistChannelId);
                var reportsChannel = ulong.Parse(config.BugReportsChannelId);
                var channels = await rest.GetChannelsAsync();
                var adminGroup = channels.OfType<ICategoryChannel>().Single(c => c.Name == ChannelLayout.AdminCategoryName);
                var usersGroup = channels.OfType<ICategoryChannel>().Single(c => c.Name == ChannelLayout.UsersCategoryName);
                var member = LiveTestMembers.Pick(await rest.GetUsersAsync().FlattenAsync())[0];
                await guild.DownloadUsersAsync();
                var memberPerms = (ulong id) => guild.GetUser(member.Id).GetPermissions(guild.GetChannel(id));

                // 1. The two channels, read back from Discord.
                var checklistRest = (ITextChannel)await host.Client.Rest.GetChannelAsync(checklistChannel);
                var reportsRest = (ITextChannel)await host.Client.Rest.GetChannelAsync(reportsChannel);
                Log($"[channels] #{checklistRest.Name} in {(checklistRest.CategoryId == adminGroup.Id ? adminGroup.Name : "?")}; the test member sees it: {memberPerms(checklistChannel).ViewChannel}");
                Log($"[channels] #{reportsRest.Name} in {(reportsRest.CategoryId == usersGroup.Id ? usersGroup.Name : "?")}; the test member sees it: {memberPerms(reportsChannel).ViewChannel}, can type: {memberPerms(reportsChannel).SendMessages}");
                checklistRest.CategoryId.Should().Be(adminGroup.Id);
                reportsRest.CategoryId.Should().Be(usersGroup.Id);

                // 2. The checklist as posted: a summary, then one card per item with Passes and Failing.
                var entries = checklist.State().Items;
                async Task<List<IMessage>> Cards() => (await checklistRest.GetMessagesAsync(100).FlattenAsync()).Where(m => m.Author.Id == host.Client.CurrentUser.Id).OrderBy(m => m.Id).ToList<IMessage>();
                var posted = await Cards();
                Log($"[checklist] {posted.Count} message(s) for {string.Join(", ", checklist.State().Versions)}: the summary, then {entries.Count} item(s)");
                Log($"[checklist]   {Text(posted[0])}");
                Log($"[checklist]   {Text(posted[1])}  buttons [{string.Join("] [", Buttons(posted[1]))}]");
                posted.Should().HaveCount(entries.Count + 1);
                Buttons(posted[1]).Should().Equal(QaText.PassLabel, QaText.FailLabel);
                Save(stateDir, "1-posted", checklist, Mocker.Resolve<IBugReportRepository>());

                FakeInteraction AsOwner(FakeInteraction i) => i.By(guild.OwnerId, "owner", "the owner").InGuild(guild.Id).InChannel(checklistChannel);
                FakeInteraction AsMember(FakeInteraction i) => i.By(member.Id, member.Username, member.GlobalName ?? member.Username).InGuild(guild.Id).InChannel(reportsChannel);

                // 3. The owner passes the first item; the test member, not an admin, can't.
                var refused = await Send(dispatcher, FakeInteraction.Button($"{QaRoutes.Check}:pass:{entries[0].Key}").By(member.Id, member.Username).InGuild(guild.Id).InChannel(checklistChannel));
                Log($"[pass] the test member presses Passes: \"{refused.LastReply.Content}\"");
                refused.LastReply.Content.Should().Be(QaText.AdminsOnly);
                var passed = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{QaRoutes.Check}:pass:{entries[0].Key}")));
                Log($"[pass] the owner presses Passes on \"{entries[0].Name}\": \"{passed.LastReply.Content}\"");
                var card = await checklistRest.GetMessageAsync(ulong.Parse(Mocker.Resolve<IQaChecklistItemRepository>().FindByKey(entries[0].Key).MessageId));
                Log($"[pass]   card now: {Text(card)}");
                Log($"[pass]   summary now: {Text((await Cards())[0])}");
                Text(card).Should().Contain("Passed by the owner");

                // 4. The owner fails the second item: the part menu, then the form; the report is linked to the item.
                var menu = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{QaRoutes.Check}:fail:{entries[1].Key}")));
                Log($"[fail] Failing on \"{entries[1].Name}\": \"{menu.LastReply.Content.Replace("\n", " | ")}\" with a menu of {menu.LastReply.SelectMenu.Options.Count} parts");
                var form = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu(menu.LastReply.SelectMenu.CustomId, "admin")));
                Log($"[fail]   picked Admin panel and commands: form \"{form.Responder.LastModal.Title}\" with [{string.Join("] [", form.Responder.LastModal.Fields.Select(f => f.Label))}]");
                var sent = await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId)
                    .WithField(QaRoutes.WhatField, "Live check: the failing button opened this report.")
                    .WithField(QaRoutes.ExpectedField, "A linked bug report (this is a test; it will be closed).")));
                Log($"[fail]   sent: \"{sent.LastReply.Content}\"");
                var failing = Mocker.Resolve<IQaChecklistItemRepository>().FindByKey(entries[1].Key);
                var failingCard = await checklistRest.GetMessageAsync(ulong.Parse(failing.MessageId));
                Log($"[fail]   card now: {Text(failingCard)}");
                Log($"[fail]   summary now: {Text((await Cards())[0])}");
                failing.State.Should().Be(QaItemState.Failing);
                failing.BugReportId.Should().NotBeNull();
                Save(stateDir, "2-failing", checklist, Mocker.Resolve<IBugReportRepository>());

                // 5. The bug reports channel: the panel, the owner's report card, and the test member's own report.
                var panel = (await reportsRest.GetMessagesAsync(20).FlattenAsync()).First(m => Buttons(m).Contains(QaText.ReportLabel));
                Log($"[reports] panel: {Text(panel)}");
                var open = await Send(dispatcher, AsMember(FakeInteraction.Button(QaRoutes.Report)));
                var memberForm = await Send(dispatcher, AsMember(FakeInteraction.SelectMenu(open.LastReply.SelectMenu.CustomId, "web-app")));
                var memberSent = await Send(dispatcher, AsMember(FakeInteraction.ModalSubmit(memberForm.Responder.LastModal.CustomId)
                    .WithField(QaRoutes.WhatField, "Live check: a report from the test member.")
                    .WithField(QaRoutes.ExpectedField, "It reaches the bug reports channel (this is a test; it will be closed).")));
                Log($"[reports] the test member's report: \"{memberSent.LastReply.Content}\"");
                var cards = (await reportsRest.GetMessagesAsync(20).FlattenAsync()).Where(m => m.Author.Id == host.Client.CurrentUser.Id).OrderBy(m => m.Id).ToList();
                foreach (var m in cards)
                {
                    Log($"[reports]   {Text(m)}");
                }

                Buttons(cards.Last()).Should().Contain(QaText.ReportLabel, "the panel stays the newest message");

                // 6. The limit: two a day here, two minutes apart.
                var again = await Send(dispatcher, AsMember(FakeInteraction.Button(QaRoutes.Report)));
                Log($"[limit] the test member presses Report a problem again: \"{again.LastReply.Content}\"");
                again.LastReply.Content.Should().StartWith("You've sent a report just now");

                // 7. Everything passes, the failing item last (after the fix): complete.
                foreach (var entry in entries.Skip(2).Append(entries[1]))
                {
                    await Send(dispatcher, AsOwner(FakeInteraction.Button($"{QaRoutes.Check}:pass:{entry.Key}")));
                }

                var state = checklist.State();
                Log($"[complete] {state.Passed} of {state.Total} passed, {state.Failing} failing, complete {state.Complete}, the last by {state.CompletedBy}");
                Log($"[complete]   summary now: {Text((await Cards())[0])}");
                state.Complete.Should().BeTrue();
                Save(stateDir, "3-complete", checklist, Mocker.Resolve<IBugReportRepository>());

                foreach (var report in Mocker.Resolve<IBugReportRepository>().All())
                {
                    Log($"[issues] report #{report.Id} ({report.Component}) by {report.ReporterName}: {report.Filing}{(report.IssueUrl != null ? " " + report.IssueUrl : string.Empty)}");
                }

                Log($"[state] saved in {stateDir}: 1-posted.json, 2-failing.json, 3-complete.json");
            }
            finally
            {
                await host.StopAsync();
            }
        }

        // What GET /api/v1/qa/checklist returns, for docker/qa-gate.py's QA_GATE_FILE.
        private static void Save(string dir, string name, QaChecklistService checklist, IBugReportRepository reports)
        {
            var state = checklist.State();
            var body = new
            {
                checklist = state,
                bugReports = reports.All().Where(r => state.Versions.Contains(r.Version))
                    .Select(r => new { r.Id, r.Component, r.Version, r.Environment, r.Created, r.ChecklistItemKey, Filing = r.Filing.ToString(), r.IssueNumber, r.IssueUrl })
            };

            File.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        private static List<string> Buttons(IMessage message) => All(message.Components).OfType<ButtonComponent>().Select(b => b.Label).ToList();

        private static string Text(IMessage message) =>
            string.Join(" / ", message.Components.OfType<ContainerComponent>().Select(c => string.Join(" ", All(c.Components).OfType<TextDisplayComponent>().Select(t => t.Content)).Replace("\n", " | ")));

        private static IEnumerable<IMessageComponent> All(IEnumerable<IMessageComponent> components) =>
            components.SelectMany(c => new[] { c }.Concat(All(c switch
            {
                ActionRowComponent row => row.Components,
                ContainerComponent container => container.Components,
                _ => Enumerable.Empty<IMessageComponent>()
            })));

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
