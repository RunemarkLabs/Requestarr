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

        // What's new, per version: both sections to the admin channel, the users' lines to the announcements channel.
        // Which versions a server shows depends on its environment (VersionPostMode): production and staging keep every
        // version for good, QA shows what staging doesn't have yet, dev only the version running now.
        Task PostVersionNotesAsync();
    }

    internal record Post(Version Version, IReadOnlyList<string> Parts);

    internal record FoundPost(Version Version, List<ChannelMessage> Messages);

    public class AnnouncementService : IAnnouncementService, IHandleAsync<DiscordReadyEvent>
    {
        // How many of a channel's newest messages are read to find this server's version posts again.
        public const int ScanLimit = 1000;

        private static readonly Regex AdminPost = new(@"^📋 \*\*Requestarr (?<version>\d+\.\d+\.\d+): what's new\*\*");

        // Users' posts carry their version since task 4d; older ones have none and are replaced where posts are rebuilt.
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

                // Dev and QA: the posts are rebuilt to show exactly the versions that apply, whatever the channels hold now
                // (after a reset they hold nothing).
                var staged = _environment.Staged ?? new HashSet<Version>();
                var news = versions.Where(v => v.HasNews && (mode == VersionPostMode.CurrentOnly ? v.Version == current : !staged.Contains(v.Version))).ToList();
                var users = _guildSetup.AnnouncementsChannelId;

                await KeepExactlyAsync(adminChannel, AdminPost, news.Select(v => AdminNotes(v, users.HasValue)).ToList());

                if (users.HasValue)
                {
                    await KeepExactlyAsync(users.Value, UsersPost, news.Where(v => v.Users.Count > 0).Select(UserNotes).ToList());
                }

                _logger.Info("What's new ({0}): {1}", mode, news.Count == 0 ? "no versions to show" : string.Join(", ", news.Select(v => v.Version)));
            }
            catch (Exception ex)
            {
                // Whatever wasn't posted is tried again on the next start.
                _logger.Warn(ex, "Could not post what's new in {0}", current);
            }
        }

        // Production and staging: each version with something in it is posted once, oldest first, and never removed.
        private async Task PostStandingAsync(DiscordGuildConfig config, ulong adminChannel, List<ChangelogVersion> versions, Version current)
        {
            var announced = Announced.Parse(config.AnnouncedVersion);
            var users = _guildSetup.AnnouncementsChannelId;
            var inAdmin = new HashSet<Version>();
            var inUsers = new HashSet<Version>();

            // First start: the full history. A server whose channels were kept (Requestarr set up again) doesn't get
            // a second copy of what's already there.
            if (announced.IsEmpty)
            {
                inAdmin = (await FindPostsAsync(adminChannel, AdminPost)).Where(p => p.Version != null).Select(p => p.Version).ToHashSet();
                inUsers = users.HasValue ? (await FindPostsAsync(users.Value, UsersPost)).Where(p => p.Version != null).Select(p => p.Version).ToHashSet() : inUsers;
            }

            foreach (var version in versions.Where(v => !announced.Contains(v.Version)))
            {
                if (version.HasNews)
                {
                    if (!inAdmin.Contains(version.Version))
                    {
                        await SendAsync(adminChannel, AdminNotes(version, users.HasValue));
                    }

                    if (users.HasValue && version.Users.Count > 0 && !inUsers.Contains(version.Version))
                    {
                        await SendAsync(users.Value, UserNotes(version));
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

        // Makes a channel's version posts exactly `wanted`, oldest first: posts for other versions are removed, a post
        // whose words changed is edited in place, and from the first post out of order on, everything is posted again.
        private async Task KeepExactlyAsync(ulong channel, Regex header, IReadOnlyList<Post> wanted)
        {
            var found = await FindPostsAsync(channel, header);
            var versions = wanted.Select(p => p.Version).ToHashSet();

            foreach (var post in found.Where(p => p.Version == null || !versions.Contains(p.Version)))
            {
                await DeleteAsync(channel, post);
            }

            var kept = found.Where(p => p.Version != null && versions.Contains(p.Version)).ToList();
            var inOrder = 0;

            while (inOrder < kept.Count && inOrder < wanted.Count && kept[inOrder].Version == wanted[inOrder].Version && kept[inOrder].Messages.Count == wanted[inOrder].Parts.Count)
            {
                inOrder++;
            }

            foreach (var post in kept.Skip(inOrder))
            {
                await DeleteAsync(channel, post);
            }

            for (var i = 0; i < inOrder; i++)
            {
                for (var part = 0; part < wanted[i].Parts.Count; part++)
                {
                    if (kept[i].Messages[part].Content != wanted[i].Parts[part])
                    {
                        await _gateway.EditMessageAsync(channel, kept[i].Messages[part].Id, new InteractionReply { Content = wanted[i].Parts[part] });
                    }
                }
            }

            foreach (var post in wanted.Skip(inOrder))
            {
                await SendAsync(channel, post);
            }
        }

        // This server's version posts in a channel, oldest first, found by their header line. A post split over several
        // messages continues in messages whose header says "(continued)".
        private async Task<List<FoundPost>> FindPostsAsync(ulong channel, Regex header)
        {
            var posts = new List<FoundPost>();

            foreach (var message in await _gateway.GetOwnMessagesAsync(channel, ScanLimit))
            {
                var match = header.Match(message.Content ?? string.Empty);

                if (!match.Success)
                {
                    continue;
                }

                var version = match.Groups["version"].Success ? Version.Parse(match.Groups["version"].Value) : null;
                var continued = message.Content.Split('\n')[0].EndsWith(DiscordText.VersionNotesContinued);

                if (continued && version != null && posts.Count > 0 && posts[^1].Version == version)
                {
                    posts[^1].Messages.Add(message);
                }
                else
                {
                    posts.Add(new FoundPost(version, new List<ChannelMessage> { message }));
                }
            }

            return posts;
        }

        private async Task SendAsync(ulong channel, Post post)
        {
            foreach (var part in post.Parts)
            {
                await _gateway.SendMessageAsync(channel, new InteractionReply { Content = part });
            }
        }

        private async Task DeleteAsync(ulong channel, FoundPost post)
        {
            foreach (var message in post.Messages)
            {
                await _gateway.DeleteMessageAsync(channel, message.Id);
            }
        }

        private static Post AdminNotes(ChangelogVersion version, bool announcementsChannel) =>
            new(version.Version, Split(DiscordText.VersionNotesForAdmins(version, announcementsChannel && version.Users.Count > 0)).ToList());

        private static Post UserNotes(ChangelogVersion version) => new(version.Version, Split(DiscordText.VersionNotesForUsers(version)).ToList());

        // Whole lines into messages under Discord's 2,000 characters; each message after the first starts with the
        // header line again, marked continued.
        private static IEnumerable<string> Split(string text)
        {
            var lines = text.Split('\n');
            var continued = lines[0] + DiscordText.VersionNotesContinued;
            var part = new List<string>();
            var length = 0;

            foreach (var line in lines)
            {
                if (part.Count > 0 && length + line.Length + 1 > DiscordLimits.MessageLength)
                {
                    yield return string.Join("\n", part);
                    part = new List<string> { continued };
                    length = continued.Length + 1;
                }

                part.Add(DiscordLimits.Truncate(line, DiscordLimits.MessageLength));
                length += line.Length + 1;
            }

            if (part.Count > 0)
            {
                yield return string.Join("\n", part);
            }
        }
    }
}
