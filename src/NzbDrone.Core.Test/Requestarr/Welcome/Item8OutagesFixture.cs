using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Gaps;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e item 8: an outage's alert (and its notice to users, if announced) is pinned while it lasts; on recovery
    // both are unpinned and removed and the "back" messages are posted, unpinned. Several outages each have their own.
    // Every outage is kept in Requestarr's own record: instance, start, end, how long, whether and by whom announced.
    [TestFixture]
    public class Item8OutagesFixture : GapScenario
    {
        private static readonly DateTime Start = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        private DateTime _now;

        private ConnectionMonitor Monitor => (ConnectionMonitor)Mocker.Resolve<IConnectionMonitor>();

        private FakeDiscordGuild.FakeChannel AdminRoom => Discord.Channels[AdminChannel];

        private FakeDiscordGuild.FakeChannel UsersRoom => Discord.Channels[ulong.Parse(Config.AnnouncementsChannelId)];

        private InteractionReply Alert(string instance) => AdminRoom.Messages.SingleOrDefault(m => m.Content != null && m.Content.StartsWith($"⚠️ **{instance} can't be reached**"));

        private ulong AlertId(string instance) => AdminRoom.MessageIds[AdminRoom.Messages.IndexOf(Alert(instance))];

        [SetUp]
        public void Clock()
        {
            _now = Start;
            Monitor.Clock = () => _now;
        }

        private async Task Minutes(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _now += TimeSpan.FromMinutes(1);
                await Monitor.CheckAllAsync();
            }
        }

        [Test]
        public async Task the_alert_is_pinned_while_it_lasts_and_removed_when_its_back()
        {
            Sonarr.Unreachable = true;
            await Monitor.CheckAllAsync();
            await Minutes(4);

            AdminRoom.Pinned.Should().ContainSingle().Which.Should().Be(AlertId("Sonarr"));

            Sonarr.Unreachable = false;
            await Minutes(1);

            Alert("Sonarr").Should().BeNull("the alert is removed once it's back");
            AdminRoom.Pinned.Should().BeEmpty();
            AdminRoom.Messages.Last().Content.Should().StartWith("✅ **Sonarr is back**");
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task an_announced_outage_pins_its_notice_and_both_go_when_its_back()
        {
            Sonarr.Unreachable = true;
            await Monitor.CheckAllAsync();
            await Minutes(31);
            await Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{ConnectionRoutes.Announce}:{Instance("Sonarr").Id}")));

            UsersRoom.Pinned.Should().ContainSingle().Which.Should().Be(UsersRoom.MessageIds.Single());

            Sonarr.Unreachable = false;
            await Minutes(1);

            UsersRoom.Pinned.Should().BeEmpty();
            UsersRoom.Messages.Should().ContainSingle().Which.Content.Should().StartWith("✅ **TV requests work again**", "the \"back\" message, not pinned");
            AdminRoom.Pinned.Should().BeEmpty();

            var outage = Mocker.Resolve<IOutageRepository>().History().Single();
            (outage.InstanceName, outage.Started, outage.Ended, outage.Duration, outage.Announced, outage.AnnouncedBy)
                .Should().Be(("Sonarr", Start, Start.AddMinutes(32), TimeSpan.FromMinutes(32), Start.AddMinutes(31), "owner"));
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task several_outages_each_have_their_own_pinned_alert()
        {
            Sonarr.Unreachable = true;
            Radarr.Unreachable = true;
            await Monitor.CheckAllAsync();
            await Minutes(4);

            AdminRoom.Pinned.Should().HaveCount(3, "Sonarr, Radarr and Radarr 4K");
            Mocker.Resolve<IOutageRepository>().History().Should().HaveCount(3).And.OnlyContain(o => o.Ended == null && o.Announced == null);

            Sonarr.Unreachable = false;
            await Minutes(1);

            AdminRoom.Pinned.Should().HaveCount(2);
            Mocker.Resolve<IOutageRepository>().History().Count(o => o.Ended != null).Should().Be(1);
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task a_blip_shorter_than_the_alert_is_not_an_outage()
        {
            Sonarr.Unreachable = true;
            await Monitor.CheckAllAsync();
            await Minutes(1);
            Sonarr.Unreachable = false;
            await Minutes(1);

            Mocker.Resolve<IOutageRepository>().History().Should().BeEmpty();
            AdminRoom.Pinned.Should().BeEmpty();
        }

        [Test]
        public async Task without_pin_messages_the_alert_still_comes_unpinned()
        {
            Discord.CannotPin = true;
            Sonarr.Unreachable = true;
            await Monitor.CheckAllAsync();
            await Minutes(4);

            Alert("Sonarr").Should().NotBeNull();
            AdminRoom.Pinned.Should().BeEmpty();
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }
    }
}
