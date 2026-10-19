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
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;
using ChannelPermissions = Requestarr.Core.Discord.Gateway.ChannelPermissions;

namespace Requestarr.Core.Test.Discord
{
    // Live check against the dedicated Discord test server from docker/secrets.env.
    // Never runs in CI (ManualTest). Run with the DISCORD_* variables exported:
    //   dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveDiscordFixture" --logger "console;verbosity=detailed"
    [TestFixture]
    [ManualTest]
    public class LiveDiscordFixture : DbTest
    {
        [Test]
        public async Task bot_comes_online_sets_up_server_and_onboards_a_member()
        {
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");

            if (string.IsNullOrWhiteSpace(token))
            {
                Assert.Ignore("DISCORD_BOT_TOKEN not set");
            }

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

            IGuildSetupService guildSetup = null;
            IUserManagementService users = null;
            IPanelService panels = null;
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => users),
                new Lazy<IPanelService>(() => panels),
                Mocker.Resolve<IEventAggregator>(),
                LogManager.GetLogger("LiveDiscord"));

            var gateway = new DiscordNetGateway(host, settings);
            Mocker.SetConstant<IDiscordClientHost>(host);
            Mocker.SetConstant<IDiscordGuildGateway>(gateway);
            guildSetup = Mocker.Resolve<GuildSetupService>();
            Mocker.SetConstant(guildSetup);
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            var dispatcher = new InteractionDispatcher(new IInteractionHandler[] { Mocker.Resolve<UserCommandHandler>() }, LogManager.GetLogger("LiveDiscord"));

            var created = new List<string>();

            try
            {
                // 1. Bot online
                await host.StartAsync(settings.Current);

                try
                {
                    await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));
                }
                catch (TimeoutException)
                {
                    // Setup may have created things before failing; still clean up below.
                    throw;
                }

                var guild = host.Client.GetGuild(settings.Current.GuildId);
                Log($"[online] bot {host.Client.CurrentUser.Username} state={host.State} server='{guild.Name}' latency={host.Client.Latency}ms");
                host.State.Should().Be(DiscordBotState.Connected);

                // 2. Admin channel and category, read back from Discord
                var config = guildSetup.GetConfig();
                var adminChannel = ulong.Parse(config.AdminChannelId);
                var category = ulong.Parse(config.RequestCategoryIds.Single());
                var adminRest = await host.Client.Rest.GetChannelAsync(adminChannel);
                var categoryRest = await host.Client.Rest.GetChannelAsync(category);
                Log($"[setup] admin channel #{adminRest} ({adminChannel}) present: {adminRest != null}");
                Log($"[setup] request category '{categoryRest}' ({category}) present: {categoryRest != null}");
                adminRest.Should().NotBeNull();
                categoryRest.Should().NotBeNull();

                // Only what this run created is cleaned up; anything already in the server (for example
                // the UAT setup) is left exactly as it was.
                var audit = Mocker.Resolve<IAuditRepository>().All().Select(a => a.Action).ToList();
                Log($"[setup] created by this run: admin channel {audit.Contains("discord.admin-channel.create")}, category {audit.Contains("discord.category.create")}, admin role {audit.Contains("discord.admin-role.create")}");

                if (audit.Contains("discord.admin-channel.create"))
                {
                    created.Add($"channel:{adminChannel}");
                }

                if (audit.Contains("discord.category.create"))
                {
                    created.Add($"channel:{category}");
                }

                // Problem 0: the admin role exists, read back from Discord.
                var adminRoleId = guildSetup.AdminRoleId.Value;
                var roleGuild = await host.Client.Rest.GetGuildAsync(guild.Id);
                var adminRoleRead = roleGuild.GetRole(adminRoleId);
                var botRole = roleGuild.Roles.Single(r => r.Tags?.BotId == host.Client.CurrentUser.Id);
                var botTop = botRole.Position;

                // Discord ranks by position, then by ID on ties: the newer role (higher ID) is lower.
                var belowBot = adminRoleRead.Position < botTop || (adminRoleRead.Position == botTop && adminRoleRead.Id > botRole.Id);
                var roleCreated = Mocker.Resolve<IAuditRepository>().All().Any(a => a.Action == "discord.admin-role.create");
                if (roleCreated)
                {
                    created.Insert(0, $"role:{adminRoleId}");
                }

                Log($"[bot] role '{botRole.Name}' Pin Messages: {botRole.Permissions.PinMessages}, Manage Messages: {botRole.Permissions.ManageMessages}");
                Log($"[bot] role raw permissions {botRole.Permissions.RawValue}; roles with Pin or Manage Messages: {string.Join(", ", roleGuild.Roles.Where(r => r.Permissions.PinMessages || r.Permissions.ManageMessages).Select(r => $"'{r.Name}'"))}");
                Log($"[admin-role] '{adminRoleRead.Name}' ({adminRoleRead.Id}) {(roleCreated ? "created by Requestarr" : "already existed")}; position {adminRoleRead.Position}, below the bot's role '{botRole.Name}' (position {botTop}): {belowBot}; permissions={adminRoleRead.Permissions.RawValue}");
                adminRoleRead.Name.Should().Be(DiscordSettings.DefaultAdminRoleName);
                belowBot.Should().BeTrue();

                var adminOverwrites = await gateway.GetChannelOverwritesAsync(adminChannel);
                Log($"[admin-role] admin channel allows the role: {adminOverwrites.Any(o => o.TargetId == adminRoleId && o.Allow.HasFlag(ChannelPermissions.ViewChannel))}");

                // Registered commands read back from Discord: exactly /request and /help (everyone) and /admin (Manage Server).
                var registered = (await host.Client.Rest.GetGuildApplicationCommands(guild.Id)).OrderBy(c => c.Name).ToList();
                var global = await host.Client.Rest.GetGlobalApplicationCommands();
                Log($"[commands] registered on the server: {string.Join(", ", registered.Select(c => "/" + c.Name))}; global: {global.Count}");
                registered.Select(c => c.Name).Should().BeEquivalentTo("admin", "help", "request");
                global.Should().BeEmpty();

                foreach (var command in registered)
                {
                    var subs = string.Join(", ", command.Options.Select(o => o.Type == global::Discord.ApplicationCommandOptionType.SubCommandGroup
                        ? $"{o.Name} [{string.Join(" ", o.Options.Select(x => x.Name))}]"
                        : o.Name));
                    Log($"[commands] /{command.Name,-8} options: {subs}");
                    var perms = command.DefaultMemberPermissions.RawValue == 0 ? "none (visible to everyone)" : string.Join(", ", command.DefaultMemberPermissions.ToList());
                    Log($"[commands] /{command.Name,-12} default member permission: {perms}");

                    if (DiscordCommandDefinitions.AdminOnly.Contains(command.Name))
                    {
                        command.DefaultMemberPermissions.ManageGuild.Should().BeTrue(command.Name);
                    }
                    else
                    {
                        command.DefaultMemberPermissions.RawValue.Should().Be(0UL, command.Name);
                    }
                }

                // 3. /user add on a real member, from the server owner in the admin channel
                var members = await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync();
                var target = members.FirstOrDefault(m => !m.IsBot && m.Id != guild.OwnerId) ?? members.First(m => m.Id == guild.OwnerId);
                Log($"[add] target member {target.Username} ({target.Id}){(target.Id == guild.OwnerId ? " (owner: no other human member)" : string.Empty)}");

                // This test's database is fresh, so a member already onboarded by the UAT stack would get a second
                // channel and role. Never do that: leave onboarded members alone and use LiveUatReadBackFixture instead.
                var existingChannels = await (await host.Client.Rest.GetGuildAsync(guild.Id)).GetTextChannelsAsync();
                if (existingChannels.Any(c => c.Name == ChannelLayout.UserChannelName(target.Username)))
                {
                    Log($"[add] skipped: {target.Username} already has a request channel in this server; nothing was created");
                    return;
                }

                var addRun = await Send(dispatcher, FakeInteraction.SlashCommand("admin user add").WithOption("member", target.Id)
                    .By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel));
                Log($"[add] outcome={addRun.Outcome} reply: {addRun.Responder.LastReply?.Content?.Replace('\n', ' ')}");
                addRun.Outcome.Should().Be(DispatchOutcome.Handled);

                var user = users.FindByDiscordId(target.Id);
                user.Should().NotBeNull();
                var userChannel = ulong.Parse(user.PrivateChannelId);
                var userRole = ulong.Parse(user.DiscordRoleId);
                created.Add($"channel:{userChannel}");
                created.Add($"role:{userRole}");

                var memberRoles = (await host.Client.Rest.GetGuildUserAsync(guild.Id, target.Id)).RoleIds;
                Log($"[add] role {userRole} assigned to member: {memberRoles.Contains(userRole)}");
                memberRoles.Should().Contain(userRole);

                // Permission settings read back from Discord (REST, not cache)
                var overwrites = await gateway.GetChannelOverwritesAsync(userChannel);
                var restGuild = await host.Client.Rest.GetGuildAsync(guild.Id);

                foreach (var o in overwrites)
                {
                    var name = o.TargetType == OverwriteTarget.Role
                        ? (o.TargetId == guild.Id ? "@everyone" : restGuild.GetRole(o.TargetId)?.Name ?? o.TargetId.ToString())
                        : $"member {(o.TargetId == host.Client.CurrentUser.Id ? "(bot) " : string.Empty)}{o.TargetId}";
                    Log($"[permissions] {name,-28} allow=[{o.Allow}] deny=[{o.Deny}]");
                }

                overwrites.Single(o => o.TargetId == guild.Id).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel);
                overwrites.Single(o => o.TargetId == userRole).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages);
                overwrites.Single(o => o.TargetId == host.Client.CurrentUser.Id).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages);

                var expected = new List<ulong> { guild.Id, userRole, host.Client.CurrentUser.Id };

                overwrites.Single(o => o.TargetId == adminRoleId).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
                expected.Add(adminRoleId);

                overwrites.Select(o => o.TargetId).Should().BeEquivalentTo(expected, "nobody else may see the channel (the owner sees all channels by Discord rule)");
                overwrites.Where(o => o.TargetId != guild.Id).Should().OnlyContain(o => o.Allow.HasFlag(ChannelPermissions.ViewChannel));
                Log("[permissions] verified: @everyone denied; only the user's role, the Requestarr Admin role, and the bot allowed");

                // Pinned panels read back from Discord.
                foreach (var (label, channelId) in new[] { ("user channel", userChannel), ("admin channel", adminChannel) })
                {
                    var channel = (global::Discord.IMessageChannel)await host.Client.Rest.GetChannelAsync(channelId);
                    var pinned = await channel.GetPinnedMessagesAsync();
                    var storedId = channelId == userChannel ? users.FindByDiscordId(target.Id).PanelMessageId : guildSetup.GetConfig().AdminPanelMessageId;
                    storedId.Should().NotBeNull($"a panel is posted in the {label}");
                    var panelId = ulong.Parse(storedId);
                    var panel = await channel.GetMessageAsync(panelId);

                    if (channelId == adminChannel && !created.Contains($"channel:{adminChannel}") && panel != null)
                    {
                        created.Add($"message:{adminChannel}:{panelId}");
                    }

                    Log($"[panel] {label}: message {panelId} present: {panel != null}, pinned: {panel?.IsPinned}");
                    panel.Should().NotBeNull();
                    panel.IsPinned.Should().BeTrue($"the bot needs Pin Messages to pin the panel in the {label}");

                    var buttons = panel.Components.OfType<global::Discord.ActionRowComponent>().SelectMany(r => r.Components).OfType<global::Discord.ButtonComponent>().Select(b => $"[{b.Label}]");
                    var lines = panel.Content.Split('\n');
                    Log($"[panel] {label}: {pinned.Count} pinned in total; \"{lines[0]}\" buttons {string.Join(" ", buttons)}");
                    Log($"[panel]   {string.Join(" | ", lines.Skip(1))}");
                }

                // 4. /user remove
                var removeRun = await Send(dispatcher, FakeInteraction.SlashCommand("admin user remove").WithOption("member", target.Id)
                    .By(guild.OwnerId, "owner").InGuild(guild.Id).InChannel(adminChannel));
                Log($"[remove] reply: {removeRun.Responder.LastReply?.Content}");

                var rolesAfter = (await host.Client.Rest.GetGuildAsync(guild.Id)).Roles.Select(r => r.Id);
                var archived = await host.Client.Rest.GetChannelAsync(userChannel);
                Log($"[remove] role deleted: {!rolesAfter.Contains(userRole)}; channel now '{archived}' with overwrites for user role: {(await gateway.GetChannelOverwritesAsync(userChannel)).Any(o => o.TargetId == userRole)}");
                rolesAfter.Should().NotContain(userRole);
                archived.ToString().Should().StartWith(ChannelLayout.ArchivedPrefix);
            }
            finally
            {
                // Remove everything the live test created: channels (user, admin, category) and commands.
                foreach (var item in created.Where(c => c.StartsWith("role:")))
                {
                    var roleId = ulong.Parse(item.Split(':')[1]);

                    if (await gateway.RoleExistsAsync(roleId))
                    {
                        await gateway.DeleteRoleAsync(roleId);
                        Log($"[cleanup] deleted role {roleId}");
                    }
                }

                foreach (var item in created.Where(c => c.StartsWith("message:")))
                {
                    var parts = item.Split(':');
                    var channel = (global::Discord.IMessageChannel)await host.Client.Rest.GetChannelAsync(ulong.Parse(parts[1]));
                    await channel.DeleteMessageAsync(ulong.Parse(parts[2]));
                    Log($"[cleanup] deleted this run's panel message {parts[2]} from the existing admin channel");
                }

                foreach (var item in created.Where(c => c.StartsWith("channel:")).Reverse())
                {
                    var id = ulong.Parse(item.Split(':')[1]);

                    try
                    {
                        if (await gateway.ChannelExistsAsync(id))
                        {
                            await gateway.DeleteChannelAsync(id);
                            Log($"[cleanup] deleted channel {id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[cleanup] could not delete channel {id}: {ex.Message}");
                    }
                }

                if (host.Client?.GetGuild(settings.Current.GuildId) is { } cleanupGuild)
                {
                    // Commands are shared with the UAT bot; the stack is restarted from main afterwards to re-register them.
                    await cleanupGuild.BulkOverwriteApplicationCommandAsync(Array.Empty<global::Discord.ApplicationCommandProperties>());
                    Log("[cleanup] removed guild slash commands (re-registered when the UAT stack restarts)");

                    var restGuild2 = await host.Client.Rest.GetGuildAsync(cleanupGuild.Id);
                    var createdIds = created.Select(c => ulong.Parse(c.Split(':').Last())).ToHashSet();
                    var channelIds = (await restGuild2.GetChannelsAsync()).Select(c => c.Id).ToHashSet();
                    var leftover = created.Count(c => (c.StartsWith("role:") && restGuild2.GetRole(ulong.Parse(c.Split(':')[1])) != null) ||
                                                      (c.StartsWith("channel:") && channelIds.Contains(ulong.Parse(c.Split(':')[1]))));
                    Log($"[cleanup] objects created by this run still present: {leftover} (of {createdIds.Count} tracked)");
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected");
            }
        }

        private static async Task<InteractionRun> Send(InteractionDispatcher dispatcher, FakeInteraction interaction)
        {
            var responder = new RecordingResponder();
            var outcome = await dispatcher.DispatchAsync(interaction.Build(), responder);
            return new InteractionRun(outcome, responder);
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
