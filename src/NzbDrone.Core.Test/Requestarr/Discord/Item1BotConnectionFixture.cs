using System;
using System.Collections.Generic;
using Discord;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Test.Discord
{
    // Item 1: bot online - provisioning, intents, health check, watchdog restart.
    [TestFixture]
    public class Item1BotConnectionFixture : CoreTest
    {
        private DateTime _now;
        private List<string> _terminations;
        private DiscordWatchdog _watchdog;

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
            _terminations = new List<string>();
            _watchdog = new DiscordWatchdog(() => _now, r => _terminations.Add(r));
        }

        [Test]
        public void should_use_only_guilds_server_members_and_server_messages_intents()
        {
            // Server messages: the bot sees its own posts so panels move below them. Message text needs the privileged
            // Message Content intent, which Requestarr doesn't ask for.
            DiscordClientHost.Intents.Should().Be(GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessages);
        }

        [Test]
        public void provisioning_should_apply_discord_settings_from_file()
        {
            var store = new DiscordSettingsStore();
            Mocker.SetConstant<IDiscordSettingsStore>(store);
            Mocker.GetMock<IProvisioningLoader>().Setup(l => l.ResolvePath()).Returns("/p.yml");
            Mocker.GetMock<IProvisioningLoader>().Setup(l => l.Load("/p.yml")).Returns(new ProvisioningDocument
            {
                Discord = new DiscordProvisioning { BotToken = "token", ApplicationId = "111", GuildId = "222", AdminRoleId = "333" }
            });

            var result = Mocker.Resolve<ProvisioningService>().Apply();

            result.Applied.Should().Contain("discord bot");
            store.Current.IsConfigured.Should().BeTrue();
            store.Current.GuildId.Should().Be(222UL);
            store.Current.AdminRoleId.Should().Be(333UL);
        }

        [Test]
        public void provisioning_without_token_should_leave_bot_not_configured()
        {
            var store = new DiscordSettingsStore();
            Mocker.SetConstant<IDiscordSettingsStore>(store);
            Mocker.GetMock<IProvisioningLoader>().Setup(l => l.ResolvePath()).Returns("/p.yml");
            Mocker.GetMock<IProvisioningLoader>().Setup(l => l.Load("/p.yml")).Returns(new ProvisioningDocument { Discord = new DiscordProvisioning() });

            Mocker.Resolve<ProvisioningService>().Apply().Applied.Should().Contain("discord (no bot token, bot not started)");
            store.Current.IsConfigured.Should().BeFalse();
        }

        [Test]
        public void loader_should_require_ids_when_token_is_set()
        {
            var loader = Mocker.Resolve<ProvisioningLoader>();
            loader.EnvironmentLookup = _ => null;

            Action act = () => loader.Parse("discord:\n  botToken: abc");

            act.Should().Throw<ProvisioningException>().WithMessage("*applicationId and discord.guildId are required*");
        }

        [TestCase(DiscordBotState.Connected, HealthCheckResult.Ok)]
        [TestCase(DiscordBotState.NotConfigured, HealthCheckResult.Warning)]
        [TestCase(DiscordBotState.Connecting, HealthCheckResult.Warning)]
        [TestCase(DiscordBotState.Disconnected, HealthCheckResult.Error)]
        [TestCase(DiscordBotState.Failed, HealthCheckResult.Error)]
        public void health_check_should_reflect_bot_state(DiscordBotState state, HealthCheckResult expected)
        {
            Mocker.GetMock<IDiscordClientHost>().SetupGet(h => h.State).Returns(state);
            Mocker.GetMock<IDiscordClientHost>().SetupGet(h => h.LastError).Returns("Authentication failed");

            var check = Mocker.Resolve<DiscordHealthCheck>().Check();

            check.Type.Should().Be(expected);

            if (expected == HealthCheckResult.Error)
            {
                check.Message.Should().Contain("Authentication failed");
            }
        }

        [Test]
        public void watchdog_should_exit_after_ten_minutes_disconnected()
        {
            _watchdog.OnConnected();
            _now = _now.AddMinutes(1);
            _watchdog.OnDisconnected();

            _now = _now.AddMinutes(9).AddSeconds(59);
            _watchdog.Check().Should().BeNull();
            _terminations.Should().BeEmpty();

            _now = _now.AddSeconds(1);
            _watchdog.Check().Should().Contain("disconnected for 10 minutes");
            _terminations.Should().HaveCount(1);
        }

        [Test]
        public void watchdog_should_not_exit_when_reconnected_in_time()
        {
            _watchdog.OnConnected();
            _watchdog.OnDisconnected();
            _now = _now.AddMinutes(8);
            _watchdog.OnConnected();
            _now = _now.AddMinutes(4);
            _watchdog.OnHeartbeat();

            _watchdog.Check().Should().BeNull();
            _terminations.Should().BeEmpty();
        }

        [Test]
        public void watchdog_should_exit_when_heartbeats_stop_while_connected()
        {
            _watchdog.OnConnected();
            _now = _now.AddMinutes(2);
            _watchdog.OnHeartbeat();
            _now = _now.AddMinutes(5);

            _watchdog.Check().Should().Contain("No Discord heartbeat");
            _terminations.Should().HaveCount(1);
        }

        [Test]
        public void watchdog_should_exit_when_never_connected()
        {
            _now = _now.AddMinutes(10);

            _watchdog.Check().Should().NotBeNull();
        }

        [Test]
        public void watchdog_should_exit_only_once()
        {
            _now = _now.AddMinutes(30);
            _watchdog.Check();
            _watchdog.Check();

            _terminations.Should().HaveCount(1);
        }

        [Test]
        public void host_should_not_start_when_discord_is_not_configured()
        {
            Mocker.SetConstant<IDiscordSettingsStore>(new DiscordSettingsStore());
            var host = Mocker.Resolve<DiscordClientHost>();

            host.HandleAsync(new ProvisioningCompletedEvent(ProvisioningResult.NoFile()));

            host.Client.Should().BeNull();
            host.State.Should().Be(DiscordBotState.NotConfigured);
        }
    }
}
