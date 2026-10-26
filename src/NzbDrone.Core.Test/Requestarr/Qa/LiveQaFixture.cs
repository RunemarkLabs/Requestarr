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
    // dev.ps1 -Stack dev down, or reset-dev --stopped). The QA checklist is posted for the running version in its own
    // Requestarr QA group; the server owner (an admin) passes an item, uses up his own report limit from the bug reports
    // panel, then fails three items in a row, each opening its own linked report; the test member reports a problem and
    // hits the limit; the bug reports channel is read back in order; then every item passes and the checklist completes.
    // With DEV_GITHUB_ISSUES_TOKEN set, reports are filed as real issues (labelled "test") and closed at the end. Clicks
    // go through the real handlers; the effects are read back from Discord. The checklist's state is saved after each
    // step in the API's format (LIVE_QA_STATE_DIR) for docker/qa-gate.py. Its own report cards are removed at the end.
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
            bugSettings.Variable = _ => null;
            bugSettings.Apply(new Requestarr.Core.Provisioning.BugReportsProvisioning { GithubToken = token, ReportsPerDay = 2, MinutesBetween = 2 });
            Mocker.SetConstant<IBugReportSettings>(bugSettings);
            Mocker.SetConstant<IGitHubIssues>(new GitHubIssues(LogManager.GetLogger("LiveGitHub")));

            IGuildSetupService guildSetup = null;
            IPanelService panels = null;
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => null),
                new Lazy<IPanelService>(() => panels),
                new Lazy<IRequestFeed>(() => Mocker.GetMock<IRequestFeed>().Object),
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

            // Warnings and errors (a handler that failed, GitHub refusing) are printed at the end.
            var problems = new NLog.Targets.MemoryTarget("live-qa") { Layout = "${level}: ${logger}: ${message} ${exception:format=tostring}" };
            LogManager.Configuration ??= new NLog.Config.LoggingConfiguration();
            LogManager.Configuration.AddRule(NLog.LogLevel.Warn, NLog.LogLevel.Fatal, problems);
            LogManager.ReconfigExistingLoggers();

            await host.StartAsync(settings.Current);
            await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));
            ulong? membersRole = null;
            IUser member = null;

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
                var qaGroup = channels.OfType<ICategoryChannel>().Single(c => c.Name == ChannelLayout.QaCategoryName);
                var usersGroup = channels.OfType<ICategoryChannel>().Single(c => c.Name == ChannelLayout.UsersCategoryName);
                member = LiveTestMembers.Pick(await rest.GetUsersAsync().FlattenAsync())[0];
                await guild.DownloadUsersAsync();
                var memberPerms = (ulong id) => guild.GetUser(member.Id).GetPermissions(guild.GetChannel(id));

                // 1. The two channels, read back from Discord. The test member, given the members role every onboarded
                // member holds (taken away again at the end), sees the bug reports channel but can't type in it, and
                // doesn't see the checklist.
                var checklistRest = (ITextChannel)await host.Client.Rest.GetChannelAsync(checklistChannel);
                var reportsRest = (ITextChannel)await host.Client.Rest.GetChannelAsync(reportsChannel);
                Log($"[channels] #{checklistRest.Name} in {(checklistRest.CategoryId == qaGroup.Id ? qaGroup.Name : "?")}; #{reportsRest.Name} in {(reportsRest.CategoryId == usersGroup.Id ? usersGroup.Name : "?")}");
                Log($"[channels] the test member before they're a member: sees the checklist {memberPerms(checklistChannel).ViewChannel}, sees bug reports {memberPerms(reportsChannel).ViewChannel}");
                membersRole = ulong.Parse(config.MembersRoleId);
                await gateway.AddRoleToMemberAsync(member.Id, membersRole.Value);
                await Task.Delay(TimeSpan.FromSeconds(2));
                Log($"[channels] the test member with the members role: sees the checklist {memberPerms(checklistChannel).ViewChannel}, sees bug reports {memberPerms(reportsChannel).ViewChannel}, can type there {memberPerms(reportsChannel).SendMessages}");
                checklistRest.CategoryId.Should().Be(qaGroup.Id);
                reportsRest.CategoryId.Should().Be(usersGroup.Id);
                memberPerms(checklistChannel).ViewChannel.Should().BeFalse();
                (memberPerms(reportsChannel).ViewChannel, memberPerms(reportsChannel).SendMessages).Should().Be((true, false));

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
                Log($"[pass] the owner presses Passes on \"{entries[0].Name}\": answered with [{string.Join(", ", passed.Responder.Responses.Select(r => r.Kind))}] and no text, the card itself changes");
                passed.Outcome.Should().Be(DispatchOutcome.Handled);
                passed.Responder.Responses.Select(r => r.Kind).Should().Equal(ResponseKind.Deferred);
                var card = await checklistRest.GetMessageAsync(ulong.Parse(Mocker.Resolve<IQaChecklistItemRepository>().FindByKey(entries[0].Key).MessageId));
                Log($"[pass]   card now: {Text(card)}");
                Log($"[pass]   summary now: {Text((await Cards())[0])}");
                Text(card).Should().Contain("Passed by the owner");

                // 4. The owner sends a report from the bug reports panel, so his own report limit is used up...
                var ownerReport = await Report(dispatcher, i => AsOwner(i).InChannel(reportsChannel), "other", "Live check: the owner's report from the panel.");
                Log($"[limit] the owner's report from the panel: answered [{string.Join(", ", ownerReport.Responder.Responses.Select(r => r.Kind))}], private menu removed {ownerReport.Responder.OriginalDeleted}");
                var ownerAgain = await Send(dispatcher, AsOwner(FakeInteraction.Button(QaRoutes.Report)).InChannel(reportsChannel));
                Log($"[limit] the owner presses Report a problem again: \"{ownerAgain.LastReply.Content}\"");
                ownerAgain.LastReply.Content.Should().StartWith("You've sent a report just now");

                // ...then fails three items in a row: each opens the part menu and the form, and links its own report.
                foreach (var entry in entries.Skip(1).Take(3))
                {
                    var menu = await Send(dispatcher, AsOwner(FakeInteraction.Button($"{QaRoutes.Check}:fail:{entry.Key}")));
                    Log($"[fail] Failing on \"{entry.Name}\": {(menu.LastReply.SelectMenu != null ? $"the part menu ({menu.LastReply.SelectMenu.Options.Count} parts)" : $"\"{menu.LastReply.Content}\"")}");
                    menu.LastReply.SelectMenu.Should().NotBeNull("an admin working through the checklist isn't held to the report limit");
                    var form = await Send(dispatcher, AsOwner(FakeInteraction.SelectMenu(menu.LastReply.SelectMenu.CustomId, "admin")));
                    var sent = await Send(dispatcher, AsOwner(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId)
                        .WithField(QaRoutes.WhatField, $"Live check: failing \"{entry.Name}\" opened this report.")
                        .WithField(QaRoutes.ExpectedField, "A linked bug report (this is a test; it will be closed).")));
                    var failing = Mocker.Resolve<IQaChecklistItemRepository>().FindByKey(entry.Key);
                    var failingCard = await checklistRest.GetMessageAsync(ulong.Parse(failing.MessageId));
                    Log($"[fail]   sent: \"{sent.LastReply.Content}\"");
                    Log($"[fail]   card now: {Text(failingCard)}");
                    failing.State.Should().Be(QaItemState.Failing);
                    failing.BugReportId.Should().NotBeNull();
                }

                Mocker.Resolve<IQaChecklistItemRepository>().All().Where(r => r.State == QaItemState.Failing).Select(r => r.BugReportId).Should().OnlyHaveUniqueItems().And.HaveCount(3);
                Log($"[fail] summary now: {Text((await Cards())[0])}");
                Save(stateDir, "2-failing", checklist, Mocker.Resolve<IBugReportRepository>());

                // 5. The bug reports channel: the panel, the owner's report card, and the test member's own report.
                var panel = (await reportsRest.GetMessagesAsync(20).FlattenAsync()).First(m => Buttons(m).Contains(QaText.ReportLabel));
                Log($"[reports] panel: {Text(panel)}");
                var memberSent = await Report(dispatcher, AsMember, "web-app", "Live check: a report from the test member.");
                Log($"[reports] the test member's report: answered [{string.Join(", ", memberSent.Responder.Responses.Select(r => r.Kind))}], private menu removed {memberSent.Responder.OriginalDeleted}");
                memberSent.Responder.OriginalDeleted.Should().BeTrue("the card is the answer; the menu replied to the panel, which moves");

                // The panel moves below new posts a few seconds after them. The channel reads top to bottom: the reports
                // in the order they were sent, then the panel, nothing replying to anything, nothing edited for nothing.
                await Task.Delay(TimeSpan.FromSeconds(8));
                var ours = Mocker.Resolve<IBugReportRepository>().All().Select(r => r.MessageId).ToHashSet();
                var cards = (await reportsRest.GetMessagesAsync(50).FlattenAsync()).Where(m => m.Author.Id == host.Client.CurrentUser.Id).OrderBy(m => m.Id).ToList();
                foreach (var m in cards)
                {
                    Log($"[reports]   {(m.EditedTimestamp != null ? "(edited) " : string.Empty)}{(m.Reference != null ? "(a reply) " : string.Empty)}{Text(m)}");
                }

                Buttons(cards.Last()).Should().Contain(QaText.ReportLabel, "the panel stays the newest message");
                cards.Last().EditedTimestamp.Should().BeNull("a moved panel is a new message, never edited");
                cards.Should().OnlyContain(m => m.Reference == null);
                var order = cards.Where(m => ours.Contains(m.Id.ToString())).Select(m => Mocker.Resolve<IBugReportRepository>().All().Single(r => r.MessageId == m.Id.ToString()).Id).ToList();
                order.Should().BeInAscendingOrder().And.HaveCount(ours.Count);

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
                    Log($"[issues] report #{report.Id} ({report.Component}) by {report.ReporterName}: {report.Filing}{(report.IssueUrl != null ? " " + report.IssueUrl : string.Empty)} | card: {QaText.Filing(report)}");
                }

                Log($"[state] saved in {stateDir}: 1-posted.json, 2-failing.json, 3-complete.json");
            }
            finally
            {
                if (membersRole != null)
                {
                    await gateway.RemoveRoleFromMemberAsync(member.Id, membersRole.Value);
                }

                // This run's report cards go (the dev stack's own reports stay), and its test issues are closed.
                foreach (var report in Mocker.Resolve<IBugReportRepository>().All())
                {
                    if (ulong.TryParse(report.ChannelId, out var channel) && ulong.TryParse(report.MessageId, out var message))
                    {
                        await gateway.DeleteMessageAsync(channel, message);
                    }

                    if (report.IssueNumber is { } number && !string.IsNullOrWhiteSpace(token))
                    {
                        using var close = GitHubIssues.Build(System.Net.Http.HttpMethod.Patch, $"repos/{bugSettings.Repository}/issues/{number}", token, new { state = "closed", state_reason = "not_planned" });
                        using var closed = await new System.Net.Http.HttpClient().SendAsync(close);
                        Log($"[issues] closed #{number}: HTTP {(int)closed.StatusCode}");
                    }
                }

                await host.StopAsync();

                foreach (var problem in problems.Logs.Where(l => !l.Contains("Discord.Net")))
                {
                    Log("[log] " + problem);
                }
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
                    .Select(r => new { r.Id, r.Component, r.Version, r.Environment, r.Created, r.ChecklistItemKey, Filing = r.Filing.ToString(), r.FilingProblem, r.IssueNumber, r.IssueUrl }),
                unfiledReports = reports.All().Where(r => r.Filing != BugReportFiling.Filed)
                    .Select(r => new { r.Id, r.Component, r.Version, r.Created, Filing = r.Filing.ToString(), r.FilingProblem, What = r.WhatHappened })
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

        // Report a problem from the panel: the part menu, the form, then the report.
        private static async Task<InteractionRun> Report(InteractionDispatcher dispatcher, Func<FakeInteraction, FakeInteraction> who, string part, string what)
        {
            var open = await Send(dispatcher, who(FakeInteraction.Button(QaRoutes.Report)));
            var form = await Send(dispatcher, who(FakeInteraction.SelectMenu(open.LastReply.SelectMenu.CustomId, part)));
            return await Send(dispatcher, who(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId)
                .WithField(QaRoutes.WhatField, what)
                .WithField(QaRoutes.ExpectedField, "It reaches the bug reports channel (this is a test; it will be closed).")));
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
