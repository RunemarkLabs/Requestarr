using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Availability;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Gaps;

namespace Requestarr.Core.Test.AvailabilityFeed
{
    // Task 3: availability (webhook and poll), the admin feed, adding seasons. Real services over
    // the fake server and backends; one clock for the request service and the availability checks.
    public abstract class FeedScenario : GapScenario
    {
        protected DateTime Now { get; set; } = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        protected AvailabilityService Availability { get; private set; }

        protected RequestService Service => (RequestService)Mocker.Resolve<IRequestService>();

        protected ulong FeedChannel => ulong.Parse(Config.FeedChannelId);

        protected FakeDiscordGuild.FakeChannel Feed => Discord.Channels[FeedChannel];

        protected InteractionReply LatestCard => Feed.Messages[^1];

        [SetUp]
        public void AvailabilityAndClock()
        {
            // NUnit keeps one fixture instance for all its tests: start every test at the same time.
            Now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

            // Requestarr's API key (the webhook tokens derive from it); built here, not a literal.
            var config = new Moq.Mock<IConfigFileProvider>();
            config.SetupGet(c => c.ApiKey).Returns(string.Concat(Enumerable.Repeat("ab", 16)));
            Mocker.SetConstant(config.Object);

            Availability = Mocker.Resolve<AvailabilityService>();
            Mocker.SetConstant<IAvailabilityService>(Availability);
            Availability.Clock = () => Now;
            Service.Clock = () => Now;
        }

        protected static bool IsCardOf(InteractionReply message, int requestId) =>
            message.Embeds.Any() && message.Embeds[0].Footer == DiscordText.FeedFooter(new Request { Id = requestId });

        protected InteractionReply CardOf(int requestId) => Feed.Messages.Single(m => IsCardOf(m, requestId));

        protected int CardsFor(int requestId) => Feed.Messages.Count(m => IsCardOf(m, requestId));

        protected static string State(InteractionReply card) => card.Embeds[0].Description.Split('\n')[0];

        protected static string[] ButtonIds(InteractionReply reply) => reply.Buttons.Select(b => b.CustomId).ToArray();

        protected Request Reload(int id) => RequestStore.Get(id);

        // A click on a request's card in the feed channel, by the server owner (or someone else).
        protected Task<InteractionRun> FeedClick(int requestId, string customId, ulong? by = null) =>
            Harness.SendAsync(FakeInteraction.Button(customId).By(by ?? FakeDiscordGuild.Owner, by.HasValue ? "someone" : "owner").InGuild(FakeDiscordGuild.Guild).InChannel(FeedChannel)
                .OnMessage(ulong.TryParse(Reload(requestId).FeedMessageId, out var card) ? card : 1));

        // Cancel on a feed card: it asks for an optional reason first (unless it's too late).
        protected async Task<InteractionRun> FeedCancel(int requestId, string reason = null)
        {
            var click = await FeedClick(requestId, $"req-admin-cancel:{requestId}");

            if (click.Responder.LastModal == null)
            {
                return click;
            }

            click.Responder.LastModal.CustomId.Should().Be($"req-cancel-reason:{requestId}");
            return await Harness.SendAsync(FakeInteraction.ModalSubmit($"req-cancel-reason:{requestId}").WithField(RequestRoutes.ReasonField, reason ?? string.Empty)
                .By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(FeedChannel));
        }

        protected Task<InteractionRun> FeedChoose(int requestId, string customId, params string[] values) =>
            Harness.SendAsync(FakeInteraction.SelectMenu(customId, values).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(FeedChannel)
                .OnMessage(ulong.Parse(Reload(requestId).FeedMessageId)));

        // A movie request sent to Radarr (pending, then approved by the admin).
        protected async Task<Request> SentMovie(ulong user = Alice, int tmdbId = Dune)
        {
            await PendingMovie(user, tmdbId);
            var request = RequestStore.All().Single(r => r.TmdbId == tmdbId);
            await AdminButton($"req-approve:{request.Id}");
            return Reload(request.Id);
        }

        // A TV request made through its own message with Choose seasons; returns the message.
        protected async Task<ulong> AskSeasons(ulong user, int tvdbId, params int[] seasons)
        {
            var message = await Ask(user, "tv", $"tvdb:{tvdbId}");
            var picker = (await ClickOn(user, message, $"req-tv-choose:{tvdbId}")).LastReply;
            var picked = (await ChooseOn(user, message, picker.SelectMenu.CustomId, seasons.Select(s => $"s{s}").ToArray())).LastReply;
            var confirm = (await ClickOn(user, message, picked.Buttons.Single(b => b.Label == DiscordText.ContinueLabel).CustomId)).LastReply;
            await ClickOn(user, message, confirm.Buttons.First().CustomId);
            return message;
        }

        protected async Task<Request> SentShow(ulong user, int tvdbId, params int[] seasons)
        {
            await AskSeasons(user, tvdbId, seasons);
            var request = RequestStore.All().Single(r => r.TvdbId == tvdbId);

            if (request.Status == RequestStatus.Pending)
            {
                await AdminButton($"req-approve:{request.Id}");
            }

            return Reload(request.Id);
        }

        protected string Token(int backendId) => WebhookTokens.For(Mocker.Resolve<IConfigFileProvider>().ApiKey, backendId);

        protected Task<WebhookOutcome> Webhook(string eventType, Request request) =>
            Availability.HandleWebhookAsync(request.BackendId.Value, Token(request.BackendId.Value), eventType, request.TvdbId, request.TmdbId);

        protected int Audits(string action) => AuditTrail.Count(a => a.Action == action);
    }
}
