using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Gaps;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.AdminRequests
{
    // Task 3c item 7: content types follow real connections. Never connected: hidden from users.
    // Lost: still shown, requests say they're paused, the admins get one alert and one "back".
    [TestFixture]
    public class Item1ConnectionsFixture : GapScenario
    {
        private DateTime _now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

        private ConnectionMonitor Monitor => (ConnectionMonitor)Mocker.Resolve<IConnectionMonitor>();

        private BackendInstance Sonarr1 => Instance("Sonarr");

        private System.Collections.Generic.List<string> AdminMessages => Discord.Channels[AdminChannel].Messages.Select(m => m.Content).ToList();

        [SetUp]
        public void Clock()
        {
            // NUnit keeps one fixture instance for all its tests: start every test at the same time.
            _now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
            Monitor.Clock = () => _now;
        }

        private async Task Tick(TimeSpan by)
        {
            _now += by;
            await Monitor.CheckAllAsync();
        }

        [Test]
        public void instances_that_answered_on_start_are_connected()
        {
            Instance("Radarr").FirstConnected.Should().NotBeNull();
            Sonarr1.FirstConnected.Should().NotBeNull();
            Mocker.Resolve<IBackendService>().EverConnected(ContentType.Tv).Should().BeTrue();
            Mocker.Resolve<IBackendService>().EverConnected(ContentType.Music).Should().BeFalse("no Lidarr has ever connected");
        }

        [Test]
        public async Task a_type_whose_instances_never_connected_is_hidden_from_users_entirely()
        {
            // A fresh server where Sonarr has never answered: TV disappears for users, even with access.
            var sonarr = Sonarr1;
            sonarr.FirstConnected = null;
            Mocker.Resolve<IBackendRepository>().Update(sonarr);
            Sonarr.Unreachable = true;
            await Monitor.CheckAllAsync();
            await SetAccess(Alice, "tv", "auto");

            var types = await Harness.SendAsync(FakeInteraction.Autocomplete("request", "type", string.Empty).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            types.Responder.Suggestions.Select(s => s.Value).Should().Equal("movie");
            Mocker.Resolve<IPanelService>().UserPanel(UserFor(Alice)).Content.Should().NotContain("TV");
            (await Harness.SendAsync(Search(Alice, "tv", "severance"))).LastReply.Content.Should().Contain(DiscordText.TypeNotAvailable(ContentType.Tv));
            AdminMessages.Should().NotContain(m => m.Contains("can't be reached"), "nothing was lost, so nothing to alert");

            // It connects for the first time: TV appears.
            Sonarr.Unreachable = false;
            await Monitor.CheckAllAsync();
            Mocker.Resolve<IPanelService>().UserPanel(UserFor(Alice)).Content.Should().Contain("TV");
        }

        [Test]
        public async Task a_lost_instance_alerts_once_after_three_minutes_and_once_when_back()
        {
            Sonarr.Unreachable = true;
            await Tick(TimeSpan.Zero);
            await Tick(TimeSpan.FromMinutes(2));
            AdminMessages.Should().NotContain(m => m.Contains("can't be reached"), "a blip shorter than 3 minutes isn't alerted");

            await Tick(TimeSpan.FromMinutes(1));
            await Tick(TimeSpan.FromMinutes(1));
            await Tick(TimeSpan.FromMinutes(10));

            AdminMessages.Count(m => m.StartsWith("⚠️ **Sonarr can't be reached**")).Should().Be(1, "one alert per outage");
            AdminMessages.Last().Should().Contain("(TV)").And.Contain("requests are paused").And.Contain("nobody's access changes");
            AdminMessages.Last().Should().Contain($"<t:{new DateTimeOffset(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds()}:f>", "down since the first failed check");

            Sonarr.Unreachable = false;
            await Tick(TimeSpan.FromMinutes(1));
            await Tick(TimeSpan.FromMinutes(1));

            AdminMessages.Count(m => m.StartsWith("✅ **Sonarr is back**")).Should().Be(1);
            AdminMessages.Last().Should().Be(DiscordText.InstanceBackAlert(Sonarr1, TimeSpan.FromMinutes(15)));
            AdminMessages.Last().Should().Contain("after 15 minutes");
            Sonarr1.DownSince.Should().BeNull();
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task a_short_blip_gets_no_alert_either_way()
        {
            Sonarr.Unreachable = true;
            await Tick(TimeSpan.Zero);
            await Tick(TimeSpan.FromMinutes(2));
            Sonarr.Unreachable = false;
            await Tick(TimeSpan.FromMinutes(1));

            AdminMessages.Should().NotContain(m => m.Contains("Sonarr can't be reached") || m.Contains("Sonarr is back"));
        }

        [Test]
        public async Task while_down_users_still_see_the_type_and_requests_say_they_are_paused()
        {
            await SetAccess(Alice, "tv", "auto");
            Sonarr.Unreachable = true;
            await Tick(TimeSpan.Zero);
            await Tick(TimeSpan.FromMinutes(5));

            Mocker.Resolve<IPanelService>().UserPanel(UserFor(Alice)).Content.Should().Contain("TV");
            var run = await Harness.SendAsync(Search(Alice, "tv", "severance"));

            run.LastReply.Content.Should().Contain(DiscordText.RequestsPaused(ContentType.Tv, "Sonarr"));
            Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(UserFor(Alice).Id).Single(a => a.ContentType == ContentType.Tv).State.Should().Be(AccessState.AutoApprove, "an outage never changes access");
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task a_down_instance_waits_on_the_admin_until_it_is_back()
        {
            Sonarr.Unreachable = true;
            await Tick(TimeSpan.Zero);
            await Tick(TimeSpan.FromMinutes(4));

            var walk = (await Admin("queue")).LastReply;
            walk.Content.Should().StartWith(DiscordText.WalkHeader(1, 1)).And.Contain("Sonarr can't be reached").And.Contain(DiscordText.CheckNowLabel);

            Sonarr.Unreachable = false;
            var check = await Harness.SendAsync(AsAdmin(FakeInteraction.Button(walk.Buttons.Single(b => b.Label == DiscordText.CheckNowLabel).CustomId)));

            check.LastReply.Content.Should().Contain(DiscordText.InstanceBackNow("Sonarr")).And.Contain(DiscordText.WalkDone);
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task the_admin_panel_counts_what_waits()
        {
            await Mocker.Resolve<IPanelService>().EnsureAdminPanelAsync();
            await PendingMovie(Alice, Dune);
            await Mocker.Resolve<IPanelService>().RefreshAdminPanelAsync();

            Discord.Channels[AdminChannel].PinnedMessage.Buttons.First().Label.Should().Be(DiscordText.PanelPendingQueueCount(1)).And.Be("Pending queue (1)");
        }
    }
}
