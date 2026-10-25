using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e: the welcome channel, its access requests and spam guard, from the point of view of someone new in the
    // server (Carol) and the admin. The clock is the test's, so cooldowns pass without waiting.
    public abstract class WelcomeScenario : DiscordScenario
    {
        public const ulong Carol = 900000000000001010;

        protected DateTime Now { get; set; } = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        protected AccessRequestService Access => (AccessRequestService)Mocker.Resolve<IAccessRequestService>();

        protected ulong WelcomeChannel => ulong.Parse(Config.WelcomeChannelId);

        protected ulong BlockedChannel => ulong.Parse(Config.BlockedChannelId);

        protected ulong BlockedRole => ulong.Parse(Config.BlockedRoleId);

        protected FakeDiscordGuild.FakeChannel AdminRoom => Discord.Channels[AdminChannel];

        [SetUp]
        public async Task SetUpWelcome()
        {
            Discord.AddMember(Carol, "carol");
            Access.Clock = () => Now;
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
        }

        // A button on the welcome message, pressed by someone in the server.
        protected Task<InteractionRun> Press(ulong member, string customId = WelcomeRoutes.Request) =>
            Harness.SendAsync(FakeInteraction.Button(customId).By(member, Discord.Members[member].Username).InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel).OnMessage(ulong.Parse(Config.WelcomeMessageId)));

        // The whole form: Request access, pick the types, Continue, and send with a note.
        protected async Task<InteractionRun> AskForAccess(ulong member, string note, params string[] types)
        {
            var click = await Press(member);

            if (click.Responder.LastReply?.SelectMenu?.CustomId != WelcomeRoutes.Types)
            {
                return click;
            }

            var picked = await Harness.SendAsync(FakeInteraction.SelectMenu(WelcomeRoutes.Types, types).By(member).InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel));
            var next = picked.LastReply.Buttons.Single(b => b.CustomId.StartsWith(WelcomeRoutes.Continue)).CustomId;
            var form = await Harness.SendAsync(FakeInteraction.Button(next).By(member).InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel));

            return await Harness.SendAsync(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId).WithField(WelcomeRoutes.NoteField, note ?? string.Empty)
                .By(member, Discord.Members[member].Username).InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel));
        }

        protected AccessRequest LastRequest => Mocker.Resolve<IAccessRequestRepository>().All().OrderBy(r => r.Id).LastOrDefault();

        // The admin's Approve…, the levels chosen per type ("movie", "approval"), then Add with this access.
        protected async Task<InteractionRun> Approve(int requestId, params (string Type, string Level)[] levels)
        {
            var chooser = (await Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{AccessRoutes.Approve}:{requestId}")))).LastReply;

            foreach (var (type, level) in levels)
            {
                var menu = new[] { chooser.SelectMenu }.Concat(chooser.MoreMenus).Single(m => m.CustomId.EndsWith(":" + type));
                chooser = (await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu(menu.CustomId, level)))).LastReply;
            }

            return await Harness.SendAsync(AsAdmin(FakeInteraction.Button(chooser.Buttons.Single().CustomId)));
        }

        protected async Task<InteractionRun> Deny(int requestId, string reason)
        {
            await Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{AccessRoutes.Deny}:{requestId}")));
            return await Harness.SendAsync(AsAdmin(FakeInteraction.ModalSubmit($"{AccessRoutes.DenyForm}:{requestId}").WithField(AccessRoutes.ReasonField, reason ?? string.Empty)));
        }

        protected InteractionReply LastDirectMessage(ulong member) => Discord.DirectMessages.TryGetValue(member, out var list) ? list.Last() : null;
    }
}
