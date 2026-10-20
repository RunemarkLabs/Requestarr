using System.Threading.Tasks;
using NUnit.Framework;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Polish;

namespace Requestarr.Core.Test.Gaps
{
    // Request messages are real messages in the fake server here, so edits made after a decision
    // (by approve, deny, cancel from anywhere) can be read back from the channel.
    public abstract class GapScenario : PolishScenario
    {
        public const int ThirtySix = 500036;
        public const int OnePiece = 81797;

        [SetUp]
        public void RealMessages()
        {
            Harness.Guild = Discord;
        }

        // /request {type} {query} by a user; returns the request message it created.
        protected async Task<ulong> Ask(ulong user, string type, string query)
        {
            var run = await Harness.SendAsync(Search(user, type, query));
            return run.Responder.MessageId.Value;
        }

        protected Task<InteractionRun> ClickOn(ulong user, ulong message, string customId) =>
            Harness.SendAsync(FakeInteraction.Button(customId).By(user).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(user)).OnMessage(message));

        protected Task<InteractionRun> ChooseOn(ulong user, ulong message, string customId, params string[] values) =>
            Harness.SendAsync(FakeInteraction.SelectMenu(customId, values).By(user).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(user)).OnMessage(message));

        // The newest message in the user's channel (where a reposted request message lands).
        protected InteractionReply Latest(ulong user) => ChannelFor(user).Messages[^1];

        protected InteractionReply Message(ulong user, ulong id)
        {
            var channel = ChannelFor(user);
            return channel.Messages[channel.MessageIds.IndexOf(id)];
        }

        // A pending movie request made through its own message, as a user would.
        protected async Task<ulong> PendingMovie(ulong user, int tmdbId)
        {
            var message = await Ask(user, "movie", $"tmdb:{tmdbId}");
            await ClickOn(user, message, $"req-make:movie:{tmdbId}");
            return message;
        }

        protected Task<InteractionRun> AdminModal(string customId, string field, string value) =>
            Harness.SendAsync(FakeInteraction.ModalSubmit(customId).WithField(field, value).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
    }
}
