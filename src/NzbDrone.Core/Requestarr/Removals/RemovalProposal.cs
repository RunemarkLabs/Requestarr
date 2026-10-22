using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Users;

namespace Requestarr.Core.Removals
{
    // Buttons on removal messages and cards.
    public static class RemovalRoutes
    {
        public const string Answer = "rm-ans";          // rm-ans:{proposal}:{y|n}, the requester's prompt
        public const string Keep = "rm-keep";           // rm-keep:{proposal}, a voter's prompt
        public const string Unkeep = "rm-unkeep";       // rm-unkeep:{proposal}
        public const string User = "rm-user";           // rm-user:{request}, the requester's request message
        public const string UserScope = "rm-user-scope"; // rm-user-scope:{request}, seasons menu
        public const string UserGo = "rm-user-go";      // rm-user-go:{request}:{scope}
        public const string Feed = "rm-feed";           // rm-feed:{request}, Delete on a feed card
        public const string Admin = "rm-adm";           // rm-adm:{backend}:{item}, the admin's removal card
        public const string AdminScope = "rm-adm-scope"; // rm-adm-scope:{backend}:{item}, seasons menu
        public const string AdminGo = "rm-adm-go";      // rm-adm-go:{backend}:{item}:{scope}
        public const string AdminForce = "rm-adm-force"; // rm-adm-force:{backend}:{item}:{scope}, asks first
        public const string AdminForceYes = "rm-adm-force-yes";
        public const string Force = "rm-force";         // rm-force:{proposal}, on feed cards (asks first)
        public const string ForceYes = "rm-force-yes";  // rm-force-yes:{proposal}
        public const string Drop = "rm-drop";           // rm-drop:{proposal}
        public const string Search = "rm-search";       // the admin panel's Remove a title
        public const string SearchForm = "rm-search-form";
        public const string SearchField = "term";
        public const string Pick = "rm-pick";           // the search results menu, value {backend}:{item}

        // A season scope in a custom ID: "all", or "1.2.3".
        public static string Encode(IReadOnlyCollection<int> seasons) => seasons == null || seasons.Count == 0 ? "all" : string.Join(".", seasons.OrderBy(s => s));

        public static List<int> Decode(string scope) =>
            scope == null || scope == "all" ? new List<int>() : scope.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Distinct().OrderBy(s => s).ToList();
    }

    public enum RemovalStatus
    {
        // Admin-initiated, owned title: the requester is asked first (silence counts as yes at the timeout).
        AwaitingRequester = 0,

        // Every user with access to the type can flag Keep it until the window closes.
        Voting = 1,

        // Called off: the admin dropped it, or the requester's No was dropped.
        Cancelled = 2,

        // Deleted on an admin's Force.
        Forced = 3,

        // Deleted: the window closed with no keep flags.
        Deleted = 4,

        // The requester said No: kept, and waits on the admin (Force or drop).
        Declined = 5,

        // The window closed with keep flags: kept, and waits on the admin (Force or drop).
        Flagged = 6,

        // Kept after flags; the admin dropped it.
        Kept = 7,

        // Sonarr or Radarr refused the deletion: waits on the admin (Force retries, or drop).
        Failed = 8
    }

    public enum RequesterResponse
    {
        Yes = 1,
        No = 2,

        // No answer by the timeout: counts as yes.
        Silent = 3
    }

    // A proposal to remove a title (or some of a show's seasons) from Sonarr / Radarr.
    public class RemovalProposal : ModelBase
    {
        public int? RequestId { get; set; }
        public ContentType ContentType { get; set; }
        public string Title { get; set; }
        public int? Year { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string PosterUrl { get; set; }
        public int? BackendId { get; set; }

        // The title's ID in that instance's library.
        public int? ExternalItemId { get; set; }

        // TV: the seasons to remove; empty means the whole title.
        public List<int> Seasons { get; set; } = new();

        public int? InitiatedByUserId { get; set; }
        public bool InitiatedByAdmin { get; set; }
        public string InitiatedByName { get; set; }

        // Owned titles: the requester (asked first when an admin starts it), their answer and their prompt.
        public int? RequesterUserId { get; set; }
        public RequesterResponse? RequesterResponse { get; set; }
        public DateTime? Answered { get; set; }
        public DateTime? AnswerBy { get; set; }
        public bool ReminderSent { get; set; }
        public string RequesterChannelId { get; set; }
        public string RequesterMessageId { get; set; }

        public RemovalStatus Status { get; set; }
        public DateTime Created { get; set; }
        public DateTime? VotingStarted { get; set; }
        public DateTime? WindowClosesAt { get; set; }
        public DateTime? Closed { get; set; }

        // Who forced or dropped it, and Sonarr's / Radarr's reason when the deletion failed.
        public int? ClosedByUserId { get; set; }
        public string ClosedByName { get; set; }
        public string Detail { get; set; }

        // Ownerless titles have no request, so their feed card lives here.
        public string FeedMessageId { get; set; }

        public bool WholeTitle => Seasons == null || Seasons.Count == 0;

        public string Label => Requests.Titles.WithYear(Title, Year);

        public int? ExternalId => ContentType == ContentType.Tv ? TvdbId : TmdbId;

        // Still in progress, or waiting on the admin: blocks another removal of the same seasons.
        public bool IsOpen => Status is RemovalStatus.AwaitingRequester or RemovalStatus.Voting or RemovalStatus.Declined or RemovalStatus.Flagged or RemovalStatus.Failed;

        // Same title on the same instance, and the scopes share a season (the whole title shares all).
        public bool Overlaps(int backendId, int itemId, IReadOnlyCollection<int> seasons) =>
            BackendId == backendId && ExternalItemId == itemId && (WholeTitle || seasons == null || seasons.Count == 0 || Seasons.Intersect(seasons).Any());
    }

    public class RemovalFlag : ModelBase
    {
        public int ProposalId { get; set; }
        public int UserId { get; set; }
        public DateTime FlaggedAt { get; set; }
    }

    // Each user's "still watching?" prompt for a vote, reposted with the outcome when it closes.
    public class RemovalPrompt : ModelBase
    {
        public int ProposalId { get; set; }
        public int UserId { get; set; }
        public string ChannelId { get; set; }
        public string MessageId { get; set; }
        public DateTime Sent { get; set; }
    }

    public interface IRemovalProposalRepository : IBasicRepository<RemovalProposal>
    {
        List<RemovalProposal> ForRequest(int requestId);
        List<RemovalProposal> WithStatus(params RemovalStatus[] statuses);
    }

    public class RemovalProposalRepository : BasicRepository<RemovalProposal>, IRemovalProposalRepository
    {
        public RemovalProposalRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<RemovalProposal> ForRequest(int requestId) => Query(p => p.RequestId == requestId);

        public List<RemovalProposal> WithStatus(params RemovalStatus[] statuses) => All().Where(p => statuses.Contains(p.Status)).ToList();
    }

    public interface IRemovalFlagRepository : IBasicRepository<RemovalFlag>
    {
        List<RemovalFlag> ForProposal(int proposalId);
    }

    public class RemovalFlagRepository : BasicRepository<RemovalFlag>, IRemovalFlagRepository
    {
        public RemovalFlagRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<RemovalFlag> ForProposal(int proposalId) => Query(f => f.ProposalId == proposalId);
    }

    public interface IRemovalPromptRepository : IBasicRepository<RemovalPrompt>
    {
        List<RemovalPrompt> ForProposal(int proposalId);
    }

    public class RemovalPromptRepository : BasicRepository<RemovalPrompt>, IRemovalPromptRepository
    {
        public RemovalPromptRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<RemovalPrompt> ForProposal(int proposalId) => Query(p => p.ProposalId == proposalId);
    }
}
