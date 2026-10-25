using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Discord
{
    // A button that answers privately shows one copy per person: pressing it again replaces the earlier copy instead of
    // stacking another under it.
    [TestFixture]
    public class PrivateCopiesFixture : TestBase
    {
        private const ulong Alice = 101;
        private const ulong Bob = 102;

        private class Handler : IInteractionHandler
        {
            public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
            {
                (InteractionKind.Button, "help"),
                (InteractionKind.Button, "queue"),
                (InteractionKind.Button, "approve"),
                (InteractionKind.SlashCommand, "help")
            };

            public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
            {
                switch (interaction.Route)
                {
                    case "queue":
                        await responder.DeferAsync(ephemeral: true);
                        await responder.FollowupAsync(new InteractionReply { Content = "Pending queue", Ephemeral = true });
                        break;
                    case "approve":
                        await responder.UpdateMessageAsync(new InteractionReply { Content = "Approved" });
                        break;
                    default:
                        await responder.RespondAsync(new InteractionReply { Content = "Help", Ephemeral = true });
                        break;
                }
            }
        }

        private InteractionHarness _harness;
        private DateTime _now;

        [SetUp]
        public void Setup()
        {
            _harness = new InteractionHarness(new Handler());
            _now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
            _harness.Dispatcher.Now = () => _now;
        }

        private Task<InteractionRun> Press(string customId, ulong user = Alice) => _harness.SendAsync(FakeInteraction.Button(customId).By(user, "someone"));

        [Test]
        public async Task pressing_the_same_button_again_removes_the_earlier_copy()
        {
            var first = await Press("help");
            var second = await Press("help");
            var third = await Press("help");

            first.Responder.PrivateRepliesDeleted.Should().BeTrue();
            second.Responder.PrivateRepliesDeleted.Should().BeTrue();
            third.Responder.PrivateRepliesDeleted.Should().BeFalse("the newest copy is the one left");
        }

        [Test]
        public async Task a_private_follow_up_is_replaced_too()
        {
            var first = await Press("queue");
            await Press("queue");

            first.Responder.PrivateRepliesDeleted.Should().BeTrue();
        }

        [Test]
        public async Task different_buttons_each_keep_their_copy()
        {
            var help = await Press("help");
            var queue = await Press("queue");

            help.Responder.PrivateRepliesDeleted.Should().BeFalse();
            queue.Responder.PrivateRepliesDeleted.Should().BeFalse();
        }

        [Test]
        public async Task someone_else_pressing_it_leaves_yours()
        {
            var alice = await Press("help", Alice);
            await Press("help", Bob);

            alice.Responder.PrivateRepliesDeleted.Should().BeFalse();
        }

        [Test]
        public async Task a_copy_older_than_discord_allows_is_left_alone()
        {
            var first = await Press("help");
            _now += InteractionDispatcher.PrivateReplyLifetime;
            var second = await Press("help");
            _now += TimeSpan.FromMinutes(1);
            await Press("help");

            first.Responder.PrivateRepliesDeleted.Should().BeFalse("Discord no longer lets the bot remove it");
            second.Responder.PrivateRepliesDeleted.Should().BeTrue();
        }

        [Test]
        public async Task buttons_that_change_their_own_message_and_typed_commands_are_not_replaced()
        {
            var approve = await Press("approve");
            await Press("approve");
            var typed = await _harness.SendAsync(FakeInteraction.SlashCommand("help").By(Alice, "someone"));
            await _harness.SendAsync(FakeInteraction.SlashCommand("help").By(Alice, "someone"));

            approve.Responder.PrivateRepliesDeleted.Should().BeFalse();
            typed.Responder.PrivateRepliesDeleted.Should().BeFalse("a typed command is asked for on purpose each time");
        }
    }
}
