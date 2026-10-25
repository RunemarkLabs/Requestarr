using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e item 5: spam protection on the welcome buttons. Only clicks that can't do anything count, within a rolling
    // hour: 5 make them wait a minute, 10 make them wait 24 hours, 5 more within 24 hours of the 10th block them. A block
    // gives the blocked role (welcome hidden, blocked channel shown), one notice to the admins, and lasts until the admin
    // lifts it (Unblock, or removing the role by hand). Rapid clicks cost one row and nothing else.
    [TestFixture]
    public class Item5SpamFixture : WelcomeScenario
    {
        private WelcomeGuard Guard => Mocker.Resolve<IWelcomeGuardRepository>().Find(Carol.ToString());

        private bool HasBlockedRole => Discord.Members[Carol].RoleIds.Contains(BlockedRole);

        private int BlockNotices => AdminRoom.Messages.Count(m => m.Content != null && m.Content.StartsWith("🚫"));

        // An open request, so every later click can't do anything.
        private async Task OpenRequest() => await AskForAccess(Carol, null, "movie");

        private async Task<string> Click() => (await Press(Carol)).LastReply.Content;

        [Test]
        public async Task the_click_that_opens_the_form_never_counts()
        {
            for (var i = 0; i < 12; i++)
            {
                (await Press(Carol)).LastReply.SelectMenu.Should().NotBeNull();
            }

            Guard.Should().BeNull("nothing was counted");
        }

        [Test]
        public async Task five_counted_clicks_wait_a_minute_and_clicks_meanwhile_only_get_the_time_left()
        {
            await OpenRequest();

            for (var i = 1; i <= 4; i++)
            {
                (await Click()).Should().Be(DiscordText.WelcomePending(Now));
            }

            (await Click()).Should().Be(DiscordText.WelcomePending(Now) + "\n" + DiscordText.WelcomeSlowDown(TimeSpan.FromMinutes(1)));

            Now = Now.AddSeconds(20);
            (await Click()).Should().Be(DiscordText.WelcomeSlowDown(TimeSpan.FromSeconds(40)));

            Now = Now.AddMinutes(1);
            (await Click()).Should().Be(DiscordText.WelcomePending(Now.AddSeconds(-80)), "the minute is over");
        }

        [Test]
        public async Task ten_counted_clicks_wait_a_day_then_five_more_block_them()
        {
            await OpenRequest();

            for (var i = 1; i <= 9; i++)
            {
                await Click();
            }

            (await Click()).Should().EndWith(DiscordText.WelcomeSlowDown(TimeSpan.FromHours(24)));
            Guard.CooldownUntil.Should().Be(Now.AddHours(24));

            for (var i = 1; i <= 4; i++)
            {
                Now = Now.AddHours(1);
                (await Click()).Should().StartWith("Please wait");
                HasBlockedRole.Should().BeFalse();
            }

            Now = Now.AddHours(1);
            (await Click()).Should().Be(DiscordText.WelcomeBlockedNow);

            HasBlockedRole.Should().BeTrue();
            Guard.Blocked.Should().Be(Now);
            BlockNotices.Should().Be(1);
            AdminRoom.Messages.Last().Content.Should().Contain($"<@{Carol}>").And.Contain(ChannelLayout.BlockedChannelName);
            AdminRoom.Messages.Last().Buttons.Single().CustomId.Should().Be($"{AccessRoutes.Unblock}:{Carol}");

            (await Click()).Should().Be(DiscordText.WelcomeBlocked, "a stale button still answers, and nothing else happens");
            BlockNotices.Should().Be(1, "one notice");
        }

        [Test]
        public void the_blocked_see_the_blocked_channel_instead_of_the_welcome_channel()
        {
            Discord.Members[Carol] = Discord.Members[Carol] with { RoleIds = new[] { BlockedRole } };

            CanSee(Carol, WelcomeChannel).Should().BeFalse();
            CanSee(Carol, BlockedChannel).Should().BeTrue();
            CanSee(Alice, WelcomeChannel).Should().BeTrue("everyone else sees the welcome channel");
            CanSee(Alice, BlockedChannel).Should().BeFalse();
        }

        [Test]
        public async Task unblock_on_the_member_card_lifts_the_block()
        {
            await BlockCarol();
            var card = (await MemberCard(Carol)).LastReply;
            card.Content.Should().Contain("Blocked from asking for access");
            card.Buttons.Select(b => b.CustomId).Should().Contain($"{AccessRoutes.Unblock}:{Carol}");

            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{AccessRoutes.Unblock}:{Carol}")));

            run.LastReply.Content.Should().Be(DiscordText.Unblocked(Carol));
            HasBlockedRole.Should().BeFalse();
            Guard.Blocked.Should().BeNull();
            (await Click()).Should().Be(DiscordText.WelcomePending(LastRequest.Requested), "back to normal, counting from zero");
            AuditTrail.Should().Contain(a => a.Action == "access.unblock");
        }

        [Test]
        public async Task removing_the_blocked_role_by_hand_unblocks_them()
        {
            await BlockCarol();

            await Discord.RemoveRoleFromMemberAsync(Carol, BlockedRole);
            await Access.MemberRolesChangedAsync(Carol, Discord.Members[Carol].RoleIds);

            Guard.Blocked.Should().BeNull();
            AuditTrail.Last(a => a.Action == "access.unblock").Detail.Should().Contain("removed in Discord");
        }

        [Test]
        public async Task a_role_removed_while_the_bot_was_away_is_noticed_on_start()
        {
            await BlockCarol();
            await Discord.RemoveRoleFromMemberAsync(Carol, BlockedRole);

            await Access.ReconcileBlocksAsync();

            Guard.Blocked.Should().BeNull();
        }

        [Test]
        public async Task rapid_clicks_cost_one_row_and_no_messages()
        {
            await OpenRequest();
            var adminMessages = AdminRoom.Messages.Count;

            for (var i = 0; i < 9; i++)
            {
                await Click();
            }

            Mocker.Resolve<IWelcomeGuardRepository>().All().Should().ContainSingle();
            Mocker.Resolve<IAccessRequestRepository>().All().Should().ContainSingle();
            Guard.Clicks().Count.Should().BeLessOrEqualTo(AccessSettings.DefaultLongCooldownClicks);
            AdminRoom.Messages.Count.Should().Be(adminMessages);
            Discord.Channels[WelcomeChannel].Messages.Should().ContainSingle();
        }

        [Test]
        public void the_numbers_are_settings_with_the_owners_defaults()
        {
            var settings = Mocker.Resolve<IAccessSettings>();

            (settings.SpamWindowMinutes, settings.ShortCooldownClicks, settings.ShortCooldownMinutes, settings.LongCooldownClicks, settings.LongCooldownHours, settings.BlockClicks, settings.BlockWithinHours, settings.DenialCooldownDays)
                .Should().Be((60, 5, 1, 10, 24, 5, 24, 7));

            settings.Apply(new Requestarr.Core.Provisioning.AccessRequestsProvisioning { DenialCooldownDays = 3, Spam = new Requestarr.Core.Provisioning.AccessSpamProvisioning { ShortCooldownClicks = 3, BlockClicks = 2 } });

            (settings.DenialCooldownDays, settings.ShortCooldownClicks, settings.BlockClicks, settings.LongCooldownClicks).Should().Be((3, 3, 2, 10));
        }

        [Test]
        public void clicks_outside_the_hour_drop_off()
        {
            var guard = new WelcomeGuard { DiscordUserId = "1" };
            var settings = Mocker.Resolve<IAccessSettings>();

            for (var i = 0; i < 4; i++)
            {
                SpamRules.Count(guard, Now.AddMinutes(i * 20), settings).Should().Be(GuardOutcome.Counted);
            }

            guard.Clicks().Should().HaveCount(3, "the first fell out of the hour");
        }

        private async Task BlockCarol()
        {
            await OpenRequest();

            for (var i = 0; i < 10; i++)
            {
                await Click();
            }

            for (var i = 0; i < 5; i++)
            {
                await Click();
            }

            HasBlockedRole.Should().BeTrue();
        }

        // Whether a member sees a channel: their roles' and @everyone's permissions there.
        private bool CanSee(ulong member, ulong channel)
        {
            var overwrites = Discord.Channels[channel].Overwrites;
            var roles = Discord.Members[member].RoleIds;
            var everyone = overwrites.SingleOrDefault(o => o.TargetId == FakeDiscordGuild.Guild);
            var visible = everyone == null || !everyone.Deny.HasFlag(ChannelPermissions.ViewChannel);

            foreach (var role in overwrites.Where(o => roles.Contains(o.TargetId)))
            {
                if (role.Deny.HasFlag(ChannelPermissions.ViewChannel))
                {
                    return false;
                }

                visible |= role.Allow.HasFlag(ChannelPermissions.ViewChannel);
            }

            return visible;
        }
    }
}
