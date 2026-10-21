using System;
using System.Collections.Generic;
using System.Linq;
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

        // What's new since the last announced version: everything to the admin channel, the user-facing
        // lines to the announcements channel (nothing to users when there are none). Once per version.
        Task PostVersionNotesAsync();
    }

    public class AnnouncementService : IAnnouncementService, IHandleAsync<DiscordReadyEvent>
    {
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserIdentityRepository _identities;
        private readonly Logger _logger;

        public AnnouncementService(IDiscordGuildGateway gateway, IGuildSetupService guildSetup, IDiscordGuildConfigRepository configs, IRequestarrUserRepository users, IUserIdentityRepository identities, Logger logger)
        {
            _gateway = gateway;
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
            var announced = Version.TryParse(config.AnnouncedVersion, out var parsed) ? parsed : null;

            if (announced != null && announced >= current)
            {
                return;
            }

            // First start with announcements (a new server, or one upgraded from before them): this version only.
            var versions = Changelog.Parse(ChangelogText())
                .Where(v => v.Version <= current && (announced == null ? v.Version == current : v.Version > announced))
                .OrderBy(v => v.Version)
                .ToList();

            try
            {
                foreach (var version in versions)
                {
                    var toUsers = version.Users.Count > 0 && _guildSetup.AnnouncementsChannelId.HasValue;

                    foreach (var part in Split(DiscordText.VersionNotesForAdmins(version, toUsers)))
                    {
                        await _gateway.SendMessageAsync(adminChannel, new InteractionReply { Content = part });
                    }

                    if (toUsers)
                    {
                        foreach (var part in Split(DiscordText.VersionNotesForUsers(version)))
                        {
                            await _gateway.SendMessageAsync(_guildSetup.AnnouncementsChannelId.Value, new InteractionReply { Content = part });
                        }
                    }

                    _logger.Info("Announced what's new in {0} ({1} for users)", version.Version, version.Users.Count);
                }
            }
            catch (Exception ex)
            {
                // Not recorded as announced: tried again on the next start.
                _logger.Warn(ex, "Could not post what's new in {0}", current);
                return;
            }

            config.AnnouncedVersion = current.ToString();
            _configs.Upsert(config);
        }

        // Whole lines into messages under Discord's 2,000 characters.
        private static IEnumerable<string> Split(string text)
        {
            var part = new List<string>();
            var length = 0;

            foreach (var line in text.Split('\n'))
            {
                if (part.Count > 0 && length + line.Length + 1 > DiscordLimits.MessageLength)
                {
                    yield return string.Join("\n", part);
                    part.Clear();
                    length = 0;
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
