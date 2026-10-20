using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    // The admin feed channel: one compact card per request. Every change deletes the old card and
    // posts a fresh one at the bottom, so the newest activity is always last.
    public interface IRequestFeed
    {
        Task PostAsync(Request request, MediaLookupResult item = null);

        // The card as it stands, for refreshing a card in place after a late click.
        InteractionReply CardFor(Request request);

        // Once per feed channel: cards for requests still waiting or in progress (not finished ones).
        Task SeedAsync();
    }

    public class RequestFeed : IRequestFeed
    {
        private readonly IDiscordGuildGateway _gateway;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IRequestRepository _requests;
        private readonly IRequestSubscriberRepository _subscribers;
        private readonly IRequestarrUserRepository _users;
        private readonly IBackendService _backends;
        private readonly Logger _logger;

        public RequestFeed(IDiscordGuildGateway gateway,
                           IDiscordGuildConfigRepository configs,
                           IRequestRepository requests,
                           IRequestSubscriberRepository subscribers,
                           IRequestarrUserRepository users,
                           IBackendService backends,
                           Logger logger)
        {
            _gateway = gateway;
            _configs = configs;
            _requests = requests;
            _subscribers = subscribers;
            _users = users;
            _backends = backends;
            _logger = logger;
        }

        public InteractionReply CardFor(Request request)
        {
            var followers = _subscribers.ForRequest(request.Id).Select(s => _users.Find(s.UserId)?.DisplayName ?? $"user {s.UserId}").ToList();
            var owner = _users.Find(request.OwnerUserId)?.DisplayName ?? $"user {request.OwnerUserId}";

            return RequestCards.FeedCard(request, null, owner, _backends.Find(request.BackendId ?? 0)?.Name, followers);
        }

        public async Task PostAsync(Request request, MediaLookupResult item = null)
        {
            if (!ulong.TryParse(Config()?.FeedChannelId, out var channel))
            {
                return;
            }

            try
            {
                var card = CardFor(request);

                if (request.PosterUrl == null && item?.PosterUrl != null)
                {
                    request.PosterUrl = item.PosterUrl;
                    card = CardFor(request);
                }

                if (ulong.TryParse(request.FeedMessageId, out var old) && !await _gateway.DeleteMessageAsync(channel, old))
                {
                    // Can't delete it (gone, or no permission): at least it loses its buttons.
                    await _gateway.EditMessageAsync(channel, old, new InteractionReply { KeepEmbeds = true });
                }

                request.FeedMessageId = (await _gateway.SendMessageAsync(channel, card)).ToString();
                _requests.SetFields(request, r => r.FeedMessageId, r => r.PosterUrl);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Request {0}: its feed card could not be posted", request.Id);
            }
        }

        public async Task SeedAsync()
        {
            var config = Config();

            if (config == null || config.FeedSeeded || config.FeedChannelId == null)
            {
                return;
            }

            var open = _requests.All()
                .Where(r => r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved)
                .OrderBy(r => r.Requested).ThenBy(r => r.Id)
                .ToList();

            foreach (var request in open)
            {
                request.FeedMessageId = null;
                await PostAsync(request);
            }

            config.FeedSeeded = true;
            _configs.Upsert(config);
            _logger.Info("Admin feed seeded with {0} open request(s)", open.Count);
        }

        private DiscordGuildConfig Config() => _configs.FindByGuild(_gateway.GuildId.ToString());
    }
}
