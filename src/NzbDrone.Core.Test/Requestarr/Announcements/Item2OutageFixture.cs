using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Gaps;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Announcements
{
    // Task 3d item 2: the admin decides. Still down after 30 minutes, the down alert offers "Announce to
    // users"; an announced outage has its recovery announced; one never announced is never announced.
    [TestFixture]
    public class Item2OutageFixture : GapScenario
    {
        private static readonly DateTime Start = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        private DateTime _now = Start;

        private ConnectionMonitor Monitor => (ConnectionMonitor)Mocker.Resolve<IConnectionMonitor>();

        private BackendInstance Sonarr1 => Instance("Sonarr");

        private List<InteractionReply> Announcements => Discord.Channels[ulong.Parse(Config.AnnouncementsChannelId)].Messages;

        private InteractionReply DownAlert => Discord.Channels[AdminChannel].Messages.Single(m => m.Content.StartsWith("⚠️ **Sonarr can't be reached**"));

        [SetUp]
        public void Clock()
        {
            _now = Start;
            Monitor.Clock = () => _now;
        }

        private async Task DownFor(TimeSpan span)
        {
            Sonarr.Unreachable = true;
            await Monitor.CheckAllAsync();

            for (var t = TimeSpan.Zero; t < span; t += TimeSpan.FromMinutes(1))
            {
                _now += TimeSpan.FromMinutes(1);
                await Monitor.CheckAllAsync();
            }
        }

        private async Task Back()
        {
            Sonarr.Unreachable = false;
            _now += TimeSpan.FromMinutes(1);
            await Monitor.CheckAllAsync();
        }

        private Task<InteractionRun> PressAnnounce() =>
            Harness.SendAsync(AsAdmin(FakeInteraction.Button(DownAlert.Buttons.Single().CustomId)));

        [Test]
        public async Task the_button_appears_only_after_thirty_minutes()
        {
            await DownFor(TimeSpan.FromMinutes(29));
            DownAlert.Buttons.Should().BeNullOrEmpty("the alert itself comes at 3 minutes; the button only at 30");

            _now += TimeSpan.FromMinutes(1);
            await Monitor.CheckAllAsync();

            DownAlert.Buttons.Single().Label.Should().Be(DiscordText.AnnounceLabel);
            DownAlert.Buttons.Single().CustomId.Should().Be($"{ConnectionRoutes.Announce}:{Sonarr1.Id}");
            DownAlert.Content.Should().Contain(DiscordText.AnnounceOffer(TimeSpan.FromMinutes(30)));
            Announcements.Should().BeEmpty("nothing reaches users until an admin decides");
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task announcing_posts_a_plain_notice_and_the_recovery_is_announced_too()
        {
            await DownFor(TimeSpan.FromMinutes(31));

            var run = await PressAnnounce();

            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().Be(DiscordText.Announced("Sonarr"));
            Announcements.Single().Content.Should().Be(DiscordText.OutageNotice(ContentType.Tv, false));
            Announcements.Single().Content.Should().StartWith("⚠️ **TV requests are paused**").And.Contain("The admin knows").And.NotContain("Sonarr", "plain words for users");
            DownAlert.Buttons.Should().BeNullOrEmpty("it can't be announced twice");
            DownAlert.Content.Should().Contain(DiscordText.AlertAnnounced("owner"));

            (await Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{ConnectionRoutes.Announce}:{Sonarr1.Id}")))).LastReply.Content.Should().Be(DiscordText.AlreadyAnnounced("Sonarr"));

            await Back();

            Announcements.Should().HaveCount(2);
            Announcements.Last().Content.Should().Be(DiscordText.OutageOverNotice(ContentType.Tv, TimeSpan.FromMinutes(32)));
            Announcements.Last().Content.Should().StartWith("✅ **TV requests work again**");
            Discord.Channels[AdminChannel].Messages.Last().Content.Should().StartWith("✅ **Sonarr is back**");
            Sonarr1.DownAnnounceMessageId.Should().BeNull();
            Sonarr1.DownAnnounceOffered.Should().BeFalse();
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task an_outage_never_announced_is_never_announced_on_recovery()
        {
            await DownFor(TimeSpan.FromMinutes(45));
            DownAlert.Buttons.Should().ContainSingle();

            await Back();

            Announcements.Should().BeEmpty();
            DownAlert.Buttons.Should().BeNullOrEmpty("the offer goes away once it's back");
            DownAlert.Content.Should().Contain(DiscordText.AlertNowBack(TimeSpan.FromMinutes(46), false));
            Discord.Channels[AdminChannel].Messages.Last().Content.Should().StartWith("✅ **Sonarr is back**");
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task the_next_outage_starts_fresh()
        {
            await DownFor(TimeSpan.FromMinutes(31));
            await PressAnnounce();
            await Back();

            await DownFor(TimeSpan.FromMinutes(5));

            Discord.Channels[AdminChannel].Messages.Last().Content.Should().StartWith("⚠️ **Sonarr can't be reached**");
            Discord.Channels[AdminChannel].Messages.Last().Buttons.Should().BeNullOrEmpty();
            Announcements.Should().HaveCount(2, "no new announcement until an admin presses it again");
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task pressing_it_after_recovery_announces_nothing()
        {
            await DownFor(TimeSpan.FromMinutes(31));
            var stale = DownAlert.Buttons.Single().CustomId;
            await Back();

            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.Button(stale)));

            run.LastReply.Content.Should().Be(DiscordText.AnnounceNotDown("Sonarr"));
            Announcements.Should().BeEmpty();
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [TestCase(20, "1 minute")]
        [TestCase(60, "1 minute")]
        [TestCase(1800, "30 minutes")]
        public void durations_read_naturally(int seconds, string expected)
        {
            DiscordText.Duration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
        }

        [Test]
        public async Task only_admins_can_announce()
        {
            await DownFor(TimeSpan.FromMinutes(31));

            var run = await Harness.SendAsync(FakeInteraction.Button(DownAlert.Buttons.Single().CustomId).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.AdminOnly);
            Announcements.Should().BeEmpty();
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }
    }
}
