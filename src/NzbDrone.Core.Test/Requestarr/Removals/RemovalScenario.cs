using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.AvailabilityFeed;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Removals
{
    // Task 4: removal voting over the fake server and backends. Alice and Bob are users with Movies and
    // TV access (needs approval); Carol joins when a test needs a third voter. One clock for everything.
    public abstract class RemovalScenario : FeedScenario
    {
        public const ulong Carol = 900000000000001004;

        protected RemovalService Removals => (RemovalService)Mocker.Resolve<IRemovalService>();

        protected IRemovalProposalRepository Proposals => Mocker.Resolve<IRemovalProposalRepository>();

        protected RemovalProposal Proposal(int id = 1) => Proposals.Get(id);

        [SetUp]
        public void RemovalClock()
        {
            Removals.Clock = () => Now;
        }

        protected async Task AddCarol(string movie = "approval")
        {
            Discord.AddMember(Carol, "carol");
            await AddUser(Carol, movie: movie);
        }

        // Dune requested by the owner, approved, and downloaded (available).
        protected async Task<Request> DownloadedMovie(ulong owner = Alice, int tmdbId = Dune)
        {
            var request = await SentMovie(owner, tmdbId);
            Radarr.Imported(request.ExternalItemId.Value);
            await Webhook("Download", request);
            return Reload(request.Id);
        }

        // Severance with every aired episode of the given seasons downloaded.
        protected async Task<Request> DownloadedShow(ulong owner = Alice, params int[] seasons)
        {
            var request = await SentShow(owner, Severance, seasons);

            foreach (var season in seasons)
            {
                Sonarr.Season(request.ExternalItemId.Value, season, aired: 3, withFiles: 3);
            }

            await Webhook("Download", request);
            return Reload(request.Id);
        }

        protected async Task Tick(TimeSpan by)
        {
            Now += by;
            await Removals.TickAsync();
        }

        // A user's "still watching?" prompt (null when they have none).
        protected InteractionReply PromptOf(ulong user, int proposal = 1)
        {
            var prompt = Mocker.Resolve<IRemovalPromptRepository>().ForProposal(proposal).SingleOrDefault(p => p.UserId == UserFor(user).Id);
            var channel = ChannelFor(user);
            var index = prompt == null ? -1 : channel.MessageIds.IndexOf(ulong.Parse(prompt.MessageId));

            return index < 0 ? null : channel.Messages[index];
        }

        protected List<InteractionReply> MessagesOf(ulong user, string containing) => ChannelFor(user).Messages.Where(m => m.Content?.Contains(containing) == true).ToList();

        // The requester's own request message, with its Remove… button.
        protected Task<InteractionRun> UserRemove(ulong user, int requestId) => Click(user, $"{RemovalRoutes.User}:{requestId}");

        protected Task<InteractionRun> UserStartVote(ulong user, int requestId, string scope = "all") => Click(user, $"{RemovalRoutes.UserGo}:{requestId}:{scope}");

        // The admin starts one from the request's feed card (Delete), then presses Start or Force.
        protected async Task<InteractionRun> AdminStart(Request request, string scope = "all", bool force = false)
        {
            var card = (await FeedClick(request.Id, $"{RemovalRoutes.Feed}:{request.Id}")).LastReply;
            var go = card.Buttons.Single(b => b.Label == (force ? DiscordText.ForceNowLabel : DiscordText.StartRemovalLabel)).CustomId;
            go = go.Substring(0, go.LastIndexOf(':')) + ":" + scope;

            if (!force)
            {
                return await Harness.SendAsync(AsAdmin(FakeInteraction.Button(go)));
            }

            var confirm = (await Harness.SendAsync(AsAdmin(FakeInteraction.Button(go)))).LastReply;
            return await Harness.SendAsync(AsAdmin(FakeInteraction.Button(confirm.Buttons.Single(b => b.Label == DiscordText.ConfirmDeleteLabel).CustomId)));
        }

        protected Task<InteractionRun> Keep(ulong user, int proposal = 1) => Click(user, $"{RemovalRoutes.Keep}:{proposal}");

        protected Task<InteractionRun> TakeBack(ulong user, int proposal = 1) => Click(user, $"{RemovalRoutes.Unkeep}:{proposal}");

        protected Task<InteractionRun> Answer(ulong user, bool yes, int proposal = 1) => Click(user, $"{RemovalRoutes.Answer}:{proposal}:{(yes ? "y" : "n")}");

        protected Task<InteractionRun> Walk() => Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin requests")));

        protected Task<InteractionRun> Press(InteractionReply card, string label) =>
            Harness.SendAsync(AsAdmin(FakeInteraction.Button(card.Buttons.Single(b => b.Label == label).CustomId)));

        protected static string Line(InteractionReply card, string starting) => card.Embeds[0].Description.Split('\n').SingleOrDefault(l => l.StartsWith(starting));
    }
}
