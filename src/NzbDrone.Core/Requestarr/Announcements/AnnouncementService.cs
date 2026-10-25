using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Announcements
{
    // Published once the bot is connected and the server is set up (channels, commands, panels).
    public class DiscordReadyEvent : IEvent
    {
    }

    public interface IAnnouncementService
    {
        // Every active member holds the members role (so they see the announcements channel); nobody else does.
        Task SyncMembersAsync();

        // Posts in the announcements channel; null when there is none or Discord refused.
        Task<ulong?> PostAsync(string content);

        // What's new, per version, as one-line cards: both sections counted in the admin channel, the users' lines in
        // the announcements channel. Which versions a server shows depends on its environment (VersionPostMode):
        // production and staging keep every version for good, QA shows what staging doesn't have yet, dev only the
        // version running now.
        Task PostVersionNotesAsync();

        // The full card a Read changes button shows, privately; long versions take more than one message.
        IReadOnlyList<InteractionReply> FullCard(VersionAudience audience, Version version);
    }

    // A version post found in a channel: a card, or (Card null) a post from before cards.
    internal record FoundPost(Version Version, List<ChannelMessage> Messages, VersionCard Card);

    public class AnnouncementService : IAnnouncementService, IHandleAsync<DiscordReadyEvent>
    {
        // How many of a channel's newest messages are read to find this server's version posts again.
        public const int ScanLimit = 1000;

        // Posts from before version cards (task 4e), found so they can be replaced by cards.
        private static readonly Regex AdminPost = new(@"^📋 \*\*Requestarr (?<version>\d+\.\d+\.\d+): what's new\*\*");

        // Users' posts carry their version since task 4d; older ones have none.
        private static readonly Regex UsersPost = new(@"^✨ \*\*What's new in Requestarr(?: (?<version>\d+\.\d+\.\d+))?\*\*");

        private readonly IDiscordGuildGateway _gateway;
        private readonly IReleaseEnvironment _environment;
        private readonly IGuildSetupService _guildSetup;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserIdentityRepository _identities;
        private readonly Logger _logger;

        public AnnouncementService(IDiscordGuildGateway gateway, IReleaseEnvironment environment, IGuildSetupService guildSetup, IDiscordGuildConfigRepository configs, IRequestarrUserRepository users, IUserIdentityRepository identities, Logger logger)
        {
            _gateway = gateway;
            _environment = environment;
            _guildSetup = guildSetup;
            _configs = configs;
            _users = users;
            _identities = identities;
            _logger = logger;
        }

        // Settable so tests (and the live check) can simulate an upgrade.
        public Func<Version> CurrentVersion { get; set; } = () => BuildInfo.Version;
        public Func<string> ChangelogText { get; set; } = Changelog.Embedded;

        public void HandleAsync(DiscordReadyEvent message)
        {
            SyncMembersAsync().GetAwaiter().GetResult();
            PostVersionNotesAsync().GetAwaiter().GetResult();
        }

        public async Task SyncMembersAsync()
        {
            if (_guildSetup.MembersRoleId is not { } role)
            {
                return;
            }

            foreach (var user in _users.All().Where(u => u.PrivateChannelId != null))
            {
                var identity = _identities.GetForUser(user.Id).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider);

                if (identity == null || !ulong.TryParse(identity.ExternalId, out var discordId))
                {
                    continue;
                }

                var member = await _gateway.GetMemberAsync(discordId);

                if (member == null)
                {
                    continue;
                }

                var should = user.Status == UserStatus.Active;
                var has = member.RoleIds.Contains(role);

                try
                {
                    if (should && !has)
                    {
                        await _gateway.AddRoleToMemberAsync(discordId, role);
                        _logger.Info("Gave {0} the members role", user.DisplayName);
                    }
                    else if (!should && has)
                    {
                        await _gateway.RemoveRoleFromMemberAsync(discordId, role);
                        _logger.Info("Took the members role from {0} ({1})", user.DisplayName, user.Status);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not update the members role for {0}", user.DisplayName);
                }
            }
        }

        public async Task<ulong?> PostAsync(string content)
        {
            if (_guildSetup.AnnouncementsChannelId is not { } channel)
            {
                return null;
            }

            try
            {
                return await _gateway.SendMessageAsync(channel, new InteractionReply { Content = content });
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not post in the announcements channel");
                return null;
            }
        }

        public async Task PostVersionNotesAsync()
        {
            var config = _guildSetup.GetConfig();

            if (config == null || !ulong.TryParse(config.AdminChannelId, out var adminChannel))
            {
                return;
            }

            var current = Changelog.Release(CurrentVersion());
            var versions = Changelog.Parse(ChangelogText()).Where(v => v.Version <= current).OrderBy(v => v.Version).ToList();
            var mode = _environment.VersionPosts;

            try
            {
                if (mode == VersionPostMode.Standing)
                {
                    await PostStandingAsync(config, adminChannel, versions, current);
                    return;
                }

                // Dev and QA: the cards are rebuilt to show exactly the versions that apply, whatever the channels hold
                // now (after a reset they hold nothing).
                var staged = _environment.Staged ?? new HashSet<Version>();
                var news = versions.Where(v => v.HasNews && (mode == VersionPostMode.CurrentOnly ? v.Version == current : !staged.Contains(v.Version))).ToList();
                var users = _guildSetup.AnnouncementsChannelId;

                await KeepExactlyAsync(adminChannel, await FindPostsAsync(adminChannel, VersionAudience.Admins), news.Select(v => VersionCards.Collapsed(v, VersionAudience.Admins)).ToList());

                if (users.HasValue)
                {
                    await KeepExactlyAsync(users.Value, await FindPostsAsync(users.Value, VersionAudience.Users), news.Where(v => v.Users.Count > 0).Select(v => VersionCards.Collapsed(v, VersionAudience.Users)).ToList());
                }

                _logger.Info("What's new ({0}): {1}", mode, news.Count == 0 ? "no versions to show" : string.Join(", ", news.Select(v => v.Version)));
            }
            catch (Exception ex)
            {
                // Whatever wasn't posted is tried again on the next start.
                _logger.Warn(ex, "Could not post what's new in {0}", current);
            }
        }

        public IReadOnlyList<InteractionReply> FullCard(VersionAudience audience, Version version)
        {
            var found = Changelog.Parse(ChangelogText()).FirstOrDefault(v => v.Version == version);

            return found == null || !found.HasNews
                ? new[] { new InteractionReply { Content = DiscordText.VersionNotFound(version), Ephemeral = true } }
                : VersionCards.Full(found, audience);
        }

        // Production and staging: each version with something in it gets a card, oldest first, and keeps it. A card
        // already in the channel isn't posted again (a server set up again with its channels kept). Posts from before
        // version cards are replaced once by cards for the whole history.
        private async Task PostStandingAsync(DiscordGuildConfig config, ulong adminChannel, List<ChangelogVersion> versions, Version current)
        {
            var announced = Announced.Parse(config.AnnouncedVersion);
            var users = _guildSetup.AnnouncementsChannelId;
            var inAdmin = await FindPostsAsync(adminChannel, VersionAudience.Admins);
            var inUsers = users.HasValue ? await FindPostsAsync(users.Value, VersionAudience.Users) : new List<FoundPost>();

            if (inAdmin.Concat(inUsers).Any(p => p.Card == null))
            {
                var history = versions.Where(v => v.HasNews).ToList();
                await KeepExactlyAsync(adminChannel, inAdmin, history.Select(v => VersionCards.Collapsed(v, VersionAudience.Admins)).ToList());

                if (users.HasValue)
                {
                    await KeepExactlyAsync(users.Value, inUsers, history.Where(v => v.Users.Count > 0).Select(v => VersionCards.Collapsed(v, VersionAudience.Users)).ToList());
                }

                _logger.Info("What's new: the history is now shown as cards ({0} versions)", history.Count);
                config.AnnouncedVersion = announced.With(versions.Select(v => v.Version).Append(current)).ToString();
                _configs.Upsert(config);
                return;
            }

            var shownAdmin = inAdmin.Select(p => p.Version).ToHashSet();
            var shownUsers = inUsers.Select(p => p.Version).ToHashSet();

            foreach (var version in versions.Where(v => !announced.Contains(v.Version)))
            {
                if (version.HasNews)
                {
                    if (!shownAdmin.Contains(version.Version))
                    {
                        await SendAsync(adminChannel, VersionCards.Collapsed(version, VersionAudience.Admins));
                    }

                    if (users.HasValue && version.Users.Count > 0 && !shownUsers.Contains(version.Version))
                    {
                        await SendAsync(users.Value, VersionCards.Collapsed(version, VersionAudience.Users));
                    }

                    _logger.Info("Announced what's new in {0} ({1} for users, {2} for admins)", version.Version, version.Users.Count, version.Admins.Count);
                }

                // Recorded one version at a time, so a failure part way never posts a version twice.
                announced = announced.With(new[] { version.Version });
                config.AnnouncedVersion = announced.ToString();
                _configs.Upsert(config);
            }

            if (!announced.Contains(current))
            {
                config.AnnouncedVersion = announced.With(new[] { current }).ToString();
                _configs.Upsert(config);
            }
        }

        // Makes a channel's version cards exactly `wanted`, oldest first: anything else that is a version post (another
        // version's card, or a post from before cards) is removed, a card whose words changed is edited in place, and
        // from the first card out of order on, everything is posted again.
        private async Task KeepExactlyAsync(ulong channel, List<FoundPost> found, IReadOnlyList<VersionCard> wanted)
        {
            var versions = wanted.Select(c => c.Version).ToHashSet();

            foreach (var post in found.Where(p => p.Card == null || !versions.Contains(p.Version)))
            {
                await DeleteAsync(channel, post);
            }

            var kept = found.Where(p => p.Card != null && versions.Contains(p.Version)).ToList();
            var inOrder = 0;

            while (inOrder < kept.Count && inOrder < wanted.Count && kept[inOrder].Version == wanted[inOrder].Version)
            {
                inOrder++;
            }

            foreach (var post in kept.Skip(inOrder))
            {
                await DeleteAsync(channel, post);
            }

            for (var i = 0; i < inOrder; i++)
            {
                if (kept[i].Card.Title != wanted[i].Title || kept[i].Card.Description != wanted[i].Description)
                {
                    await _gateway.EditMessageAsync(channel, kept[i].Messages[0].Id, wanted[i].Reply);
                }
            }

            foreach (var card in wanted.Skip(inOrder))
            {
                await SendAsync(channel, card);
            }
        }

        // This server's version posts in a channel, oldest first: cards (found by their title), and posts from before
        // cards (found by their header line; one split over several messages continues in "(continued)" messages).
        private async Task<List<FoundPost>> FindPostsAsync(ulong channel, VersionAudience audience)
        {
            var posts = new List<FoundPost>();
            var legacy = audience == VersionAudience.Admins ? AdminPost : UsersPost;

            foreach (var message in await _gateway.GetOwnMessagesAsync(channel, ScanLimit))
            {
                if (VersionCards.Parse(message.EmbedTitle, audience) is { } version)
                {
                    posts.Add(new FoundPost(version, new List<ChannelMessage> { message }, new VersionCard(version, message.EmbedTitle, message.EmbedDescription, null)));
                    continue;
                }

                var match = legacy.Match(message.Content ?? string.Empty);

                if (!match.Success)
                {
                    continue;
                }

                var old = match.Groups["version"].Success ? Version.Parse(match.Groups["version"].Value) : null;
                var continued = message.Content.Split('\n')[0].EndsWith(" (continued)");

                if (continued && old != null && posts.Count > 0 && posts[^1].Version == old && posts[^1].Card == null)
                {
                    posts[^1].Messages.Add(message);
                }
                else
                {
                    posts.Add(new FoundPost(old, new List<ChannelMessage> { message }, null));
                }
            }

            return posts;
        }

        private async Task SendAsync(ulong channel, VersionCard card)
        {
            await _gateway.SendMessageAsync(channel, card.Reply);
        }

        private async Task DeleteAsync(ulong channel, FoundPost post)
        {
            foreach (var message in post.Messages)
            {
                await _gateway.DeleteMessageAsync(channel, message.Id);
            }
        }
    }
}
