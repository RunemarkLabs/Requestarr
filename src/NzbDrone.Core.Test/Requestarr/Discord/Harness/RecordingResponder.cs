using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Test.Discord.Harness
{
    public enum ResponseKind
    {
        Deferred,
        Response,
        Followup,
        MessageUpdate,
        Edit,
        Modal,
        Suggestions
    }

    public record RecordedResponse(ResponseKind Kind, InteractionReply Reply, bool Ephemeral);

    // Captures what a handler sent back, in order. Enforces Discord's rule that an
    // interaction gets exactly one initial response (respond, defer, or update).
    public class RecordingResponder : IInteractionResponder
    {
        private readonly List<RecordedResponse> _responses = new();
        private readonly FakeDiscordGuild _guild;
        private readonly ulong _channelId;

        public RecordingResponder()
        {
        }

        // With a fake server: the command's response, or the component's message, is a real
        // message in that channel, so later edits by Requestarr can be read back.
        public RecordingResponder(FakeDiscordGuild guild, ulong channelId, ulong? messageId, bool isCommand)
        {
            _guild = guild;
            _channelId = channelId;
            MessageId = messageId;
            _createsMessage = isCommand;
        }

        private readonly bool _createsMessage;

        public ulong? MessageId { get; private set; }

        public Task<ulong?> MessageIdAsync() => Task.FromResult(MessageId);

        private void Materialise(InteractionReply reply)
        {
            if (_guild == null || reply == null || reply.Ephemeral || !_guild.Channels.ContainsKey(_channelId))
            {
                return;
            }

            if (MessageId.HasValue && _guild.EditMessageAsync(_channelId, MessageId.Value, reply).Result)
            {
                return;
            }

            if (_createsMessage && !MessageId.HasValue)
            {
                MessageId = _guild.SendMessageAsync(_channelId, reply).Result;
            }
        }

        public IReadOnlyList<RecordedResponse> Responses => _responses;

        public bool HasResponded { get; private set; }

        public IEnumerable<InteractionReply> Replies => _responses.Where(r => r.Reply != null).Select(r => r.Reply);

        public InteractionReply LastReply => Replies.LastOrDefault();

        public Task DeferAsync(bool ephemeral = false)
        {
            EnsureFirstResponse("defer");
            _responses.Add(new RecordedResponse(ResponseKind.Deferred, null, ephemeral));
            return Task.CompletedTask;
        }

        public Task RespondAsync(InteractionReply reply)
        {
            EnsureFirstResponse("respond");
            _responses.Add(new RecordedResponse(ResponseKind.Response, reply, reply.Ephemeral));
            Materialise(reply);
            return Task.CompletedTask;
        }

        public Task FollowupAsync(InteractionReply reply)
        {
            if (!HasResponded)
            {
                throw new InvalidOperationException("Discord rejects a followup before the interaction has been responded to or deferred");
            }

            _responses.Add(new RecordedResponse(ResponseKind.Followup, reply, reply.Ephemeral));
            return Task.CompletedTask;
        }

        public Task UpdateMessageAsync(InteractionReply reply)
        {
            EnsureFirstResponse("update the message");
            EnsureLayoutKept(reply);
            _responses.Add(new RecordedResponse(ResponseKind.MessageUpdate, reply, false));
            Materialise(reply);
            return Task.CompletedTask;
        }

        public Task EditAsync(InteractionReply reply)
        {
            if (!HasResponded)
            {
                throw new InvalidOperationException("Discord has no original response to edit before the interaction is deferred");
            }

            EnsureLayoutKept(reply);

            _responses.Add(new RecordedResponse(ResponseKind.Edit, reply, false));
            Materialise(reply);
            return Task.CompletedTask;
        }

        public InteractionModal LastModal { get; private set; }

        public IReadOnlyList<InteractionChoice> Suggestions { get; private set; }

        public Task SuggestAsync(IReadOnlyList<InteractionChoice> choices)
        {
            EnsureFirstResponse("suggest");
            Suggestions = choices;
            _responses.Add(new RecordedResponse(ResponseKind.Suggestions, null, true));
            return Task.CompletedTask;
        }

        public Task ShowModalAsync(InteractionModal modal)
        {
            EnsureFirstResponse("show a modal");
            LastModal = modal;
            _responses.Add(new RecordedResponse(ResponseKind.Modal, null, true));
            return Task.CompletedTask;
        }

        public bool HasPrivateReplies => _responses.Any(r => r.Ephemeral && (r.Kind == ResponseKind.Response || r.Kind == ResponseKind.Followup));

        // Set once Requestarr removes this interaction's private replies (a later press of the same button).
        public bool PrivateRepliesDeleted { get; private set; }

        public Task DeletePrivateRepliesAsync()
        {
            PrivateRepliesDeleted = true;
            return Task.CompletedTask;
        }

        // Set once Requestarr removes the private message the component belongs to.
        public bool OriginalDeleted { get; private set; }

        public Task DeleteOriginalAsync()
        {
            if (!HasResponded)
            {
                throw new InvalidOperationException("Discord has no original response to delete before the interaction is answered");
            }

            OriginalDeleted = true;
            return Task.CompletedTask;
        }

        // Discord refuses to turn a message with cards (the newer layout) back into a plain one.
        private void EnsureLayoutKept(InteractionReply reply)
        {
            if (_guild != null && MessageId.HasValue && _guild.Channels.TryGetValue(_channelId, out var channel) &&
                channel.MessageIds.IndexOf(MessageId.Value) is var index and >= 0 && channel.Messages[index].IsCards && !reply.IsCards)
            {
                throw new InvalidOperationException("MESSAGE_CANNOT_REMOVE_COMPONENTS_V2_FLAG: a message with cards can't become a plain message");
            }
        }

        private void EnsureFirstResponse(string action)
        {
            if (HasResponded)
            {
                throw new InvalidOperationException($"Discord rejects a second initial response: cannot {action} after the interaction was already acknowledged");
            }

            HasResponded = true;
        }
    }
}
