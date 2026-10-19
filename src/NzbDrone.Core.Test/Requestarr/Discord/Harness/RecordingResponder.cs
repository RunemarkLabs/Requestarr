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
        Modal
    }

    public record RecordedResponse(ResponseKind Kind, InteractionReply Reply, bool Ephemeral);

    // Captures what a handler sent back, in order. Enforces Discord's rule that an
    // interaction gets exactly one initial response (respond, defer, or update).
    public class RecordingResponder : IInteractionResponder
    {
        private readonly List<RecordedResponse> _responses = new();

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
            _responses.Add(new RecordedResponse(ResponseKind.MessageUpdate, reply, false));
            return Task.CompletedTask;
        }

        public InteractionModal LastModal { get; private set; }

        public Task ShowModalAsync(InteractionModal modal)
        {
            EnsureFirstResponse("show a modal");
            LastModal = modal;
            _responses.Add(new RecordedResponse(ResponseKind.Modal, null, true));
            return Task.CompletedTask;
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
