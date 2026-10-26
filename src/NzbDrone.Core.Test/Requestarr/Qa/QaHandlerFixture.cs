using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Qa;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Qa
{
    // The checklist's buttons and the bug report flow, answered the way Discord accepts: a card stays a card (pressing
    // Passes on one used to fail every time), an admin failing items isn't held to the members' report limit, and a
    // report from the bug reports channel leaves no private reply pointing at the panel that moves below it.
    [TestFixture]
    public class QaHandlerFixture : CoreTest<QaHandler>
    {
        private const ulong Channel = 700;
        private readonly QaChecklistEntry _item = new(new Version(0, 4, 16), "0.4.16/one", "One", "Do it. You should see it.");
        private FakeDiscordGuild _guild;
        private ulong _card;
        private bool _admin;

        [SetUp]
        public void Setup()
        {
            _guild = new FakeDiscordGuild();
            _guild.Channels[Channel] = new FakeDiscordGuild.FakeChannel { Id = Channel, Name = QaText.ChecklistChannelName };
            _card = _guild.SendMessageAsync(Channel, new InteractionReply { Cards = { new InteractionCard { Text = "**One** · 0.4.16" } } }).Result;
            _admin = true;

            Mocker.GetMock<ICallerResolver>().Setup(c => c.Resolve(It.IsAny<DiscordInteraction>())).Returns(() => new CallerContext { RightServer = true, IsAdmin = _admin });
            Mocker.GetMock<IQaChecklistService>().Setup(c => c.Find(It.IsAny<string>())).Returns<string>(k => k.StartsWith("0.4.16/") ? new QaChecklistEntry(_item.Version, k, "Item " + k, _item.Text) : null);

            // The member limit is reached: only an admin's checklist work goes through.
            Mocker.GetMock<IBugReportService>().Setup(r => r.WaitLeft(It.IsAny<ulong>())).Returns(TimeSpan.FromMinutes(1));
            Mocker.GetMock<IBugReportService>()
                .Setup(r => r.SubmitAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new BugReportResult(new BugReport { Id = 1 }, "filed"));
        }

        [Test]
        public async Task passes_on_a_card_records_the_pass_and_never_turns_the_card_into_text()
        {
            var responder = await Press($"{QaRoutes.Check}:pass:{_item.Key}");

            responder.Responses.Select(r => r.Kind).Should().Equal(ResponseKind.Deferred);
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.MarkPassedAsync(_item.Key, It.IsAny<ulong>(), It.IsAny<string>()), Times.Once());
            _guild.Channels[Channel].Messages.Single().IsCards.Should().BeTrue();
        }

        [Test]
        public async Task an_admin_fails_three_items_in_a_row_each_opening_its_own_form_despite_the_member_limit()
        {
            foreach (var key in new[] { "0.4.16/one", "0.4.16/two", "0.4.16/three" })
            {
                var menu = (await Press($"{QaRoutes.Check}:fail:{key}")).LastReply;
                menu.SelectMenu.CustomId.Should().Be($"{QaRoutes.Part}:{key}");

                var form = await Send(FakeInteraction.ModalSubmit($"{QaRoutes.Form}:admin:{key}").WithField(QaRoutes.WhatField, "Broken " + key));
                form.LastReply.Content.Should().Be("filed");
            }

            Mocker.GetMock<IBugReportService>().Verify(r => r.SubmitAsync(It.IsAny<ulong>(), It.IsAny<string>(), "admin", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false), Times.Exactly(3));
            Mocker.GetMock<IBugReportService>().Verify(r => r.SubmitAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public async Task a_member_pressing_report_a_problem_still_waits_out_the_limit()
        {
            _admin = false;

            var responder = await Press(QaRoutes.Report);

            responder.LastReply.Content.Should().Contain("You can send another");
            responder.LastReply.SelectMenu.Should().BeNull();
        }

        [Test]
        public async Task a_report_from_the_bug_reports_channel_replaces_its_private_menu_and_then_removes_it()
        {
            var responder = await Send(FakeInteraction.ModalSubmit($"{QaRoutes.Form}:requests").WithField(QaRoutes.WhatField, "Nothing happened"));

            responder.Responses[0].Should().BeEquivalentTo(new { Kind = ResponseKind.MessageUpdate, Reply = new { Content = QaText.Sending } });
            responder.OriginalDeleted.Should().BeTrue("the card in the channel is the answer, and the menu replied to the panel that moves");
            Mocker.GetMock<IBugReportService>().Verify(r => r.SubmitAsync(It.IsAny<ulong>(), It.IsAny<string>(), "requests", "Nothing happened", null, null, true), Times.Once());
        }

        [Test]
        public async Task a_report_held_back_by_the_limit_says_how_long_to_wait_instead()
        {
            Mocker.GetMock<IBugReportService>().Setup(r => r.SubmitAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), null, true))
                .ReturnsAsync(new BugReportResult(null, "You've sent a report just now. You can send another in a moment."));

            var responder = await Send(FakeInteraction.ModalSubmit($"{QaRoutes.Form}:requests").WithField(QaRoutes.WhatField, "Again"));

            responder.OriginalDeleted.Should().BeFalse();
            responder.LastReply.Content.Should().Contain("You can send another");
        }

        private Task<RecordingResponder> Press(string customId) => Send(FakeInteraction.Button(customId).OnMessage(_card));

        private async Task<RecordingResponder> Send(FakeInteraction interaction)
        {
            var built = interaction.InChannel(Channel).Build();
            var responder = new RecordingResponder(_guild, Channel, built.MessageId, isCommand: false);
            await Subject.HandleAsync(built, responder);
            return responder;
        }
    }
}
