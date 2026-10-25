using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Discord
{
    // Whether Requestarr keeps the bot's own Discord picture and banner up to date. Kept in Requestarr's settings (the
    // admin web portal reads and changes them there); provisioning sets the ones it names on every start.
    public interface IBotProfileSettings
    {
        bool KeepUpToDate { get; }

        // Requestarr's picture and banner always win: one someone else chose is replaced too.
        bool TakeOver { get; }

        void Apply(BotProfileProvisioning provisioning);
    }

    public class BotProfileSettings : IBotProfileSettings
    {
        private readonly IConfigService _config;

        public BotProfileSettings(IConfigService config)
        {
            _config = config;
        }

        public bool KeepUpToDate => _config.BotProfileKeepUpToDate;

        public bool TakeOver => _config.BotProfileTakeOver;

        public void Apply(BotProfileProvisioning provisioning)
        {
            if (provisioning?.KeepUpToDate is { } keep)
            {
                _config.BotProfileKeepUpToDate = keep;
            }

            if (provisioning?.TakeOver is { } takeOver)
            {
                _config.BotProfileTakeOver = takeOver;
            }
        }
    }

    public enum BotImageAction
    {
        // Already Requestarr's current picture.
        UpToDate,

        // Set Requestarr's picture: the bot has none, Requestarr's own is out of date, or taking over.
        Set,

        // Someone chose this picture (changed by hand, or there before Requestarr managed it): left as it is.
        LeftAlone
    }

    // What Requestarr set last for one image: Discord's id for the picture it then had, and the SHA-256 of the file it
    // uploaded. Discord gives a picture a new id whenever it changes, so a different id now means someone changed it.
    public record BotImageRecord(string DiscordId, string File)
    {
        public static BotImageRecord Parse(string stored) =>
            stored?.Split('|') is [var id, var file] && id.Length > 0 && file.Length > 0 ? new BotImageRecord(id, file) : null;

        public override string ToString() => $"{DiscordId}|{File}";
    }

    // Keeps the bot's picture (the plain R, the same everywhere) and banner (the environment's) current. Runs when the
    // bot is ready, changes only what differs, and never touches a picture someone else chose. Discord limits how often
    // a picture can change; Requestarr changes each at most once per release that brings a new one, and if Discord
    // refuses anyway it logs that and tries again on a later start. Nothing here can stop the bot.
    public class BotProfileService : IHandleAsync<DiscordReadyEvent>
    {
        public const string AvatarFile = "requestarr-avatar.png";

        private readonly IDiscordClientHost _host;
        private readonly IBotProfileSettings _settings;
        private readonly IConfigService _config;
        private readonly IReleaseEnvironment _environment;
        private readonly Logger _logger;

        public BotProfileService(IDiscordClientHost host, IBotProfileSettings settings, IConfigService config, IReleaseEnvironment environment, Logger logger)
        {
            _host = host;
            _settings = settings;
            _config = config;
            _environment = environment;
            _logger = logger;
        }

        // The environment's banner: dev, QA, beta for staging, and the teal one for production and anything else.
        public static string BannerFile(string iconSet) => $"requestarr-banner-{(iconSet is "dev" or "qa" or "beta" ? iconSet : "teal")}.png";

        public static string Digest(byte[] file) => Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();

        public static BotImageAction Decide(string current, BotImageRecord record, string file, bool takeOver)
        {
            if (current == null)
            {
                return BotImageAction.Set;
            }

            if (record == null || record.DiscordId != current)
            {
                return takeOver ? BotImageAction.Set : BotImageAction.LeftAlone;
            }

            return record.File == file ? BotImageAction.UpToDate : BotImageAction.Set;
        }

        public void HandleAsync(DiscordReadyEvent message)
        {
            SyncAsync().GetAwaiter().GetResult();
        }

        public async Task SyncAsync()
        {
            if (!_settings.KeepUpToDate)
            {
                _logger.Info("Bot picture and banner: not kept up to date (botProfile.keepUpToDate is off)");
                return;
            }

            try
            {
                var client = _host.Client;

                if (client?.CurrentUser == null)
                {
                    return;
                }

                var avatar = DiscordAssets.Bytes(AvatarFile);
                var banner = DiscordAssets.Bytes(BannerFile(_environment.IconSet));
                var me = await client.Rest.GetUserAsync(client.CurrentUser.Id);

                var setAvatar = Decide(me.AvatarId, BotImageRecord.Parse(_config.BotProfileAvatarSet), Digest(avatar), _settings.TakeOver);
                var setBanner = Decide(me.BannerId, BotImageRecord.Parse(_config.BotProfileBannerSet), Digest(banner), _settings.TakeOver);

                _logger.Info("Bot picture: {0}; banner ({1}): {2}", Describe(setAvatar), BannerFile(_environment.IconSet), Describe(setBanner));

                if (setAvatar != BotImageAction.Set && setBanner != BotImageAction.Set)
                {
                    return;
                }

                using var avatarStream = new MemoryStream(avatar);
                using var bannerStream = new MemoryStream(banner);

                await client.CurrentUser.ModifyAsync(p =>
                {
                    if (setAvatar == BotImageAction.Set)
                    {
                        p.Avatar = new Image(avatarStream);
                    }

                    if (setBanner == BotImageAction.Set)
                    {
                        p.Banner = new Image(bannerStream);
                    }
                });

                // Remember Discord's ids for what was just set, so a later change by hand can be told apart.
                var now = await client.Rest.GetUserAsync(client.CurrentUser.Id);

                if (setAvatar == BotImageAction.Set)
                {
                    _config.BotProfileAvatarSet = new BotImageRecord(now.AvatarId, Digest(avatar)).ToString();
                }

                if (setBanner == BotImageAction.Set)
                {
                    _config.BotProfileBannerSet = new BotImageRecord(now.BannerId, Digest(banner)).ToString();
                }

                _logger.Info("Bot picture and banner updated in Discord");
            }
            catch (HttpException ex)
            {
                _logger.Warn("Discord refused to update the bot's picture or banner ({0}: {1}); Requestarr tries again on a later start", ex.HttpCode, string.Join(" ", new[] { ex.Reason }.Concat(ex.Errors?.SelectMany(e => e.Errors.Select(x => x.Message)) ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s))));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Couldn't update the bot's picture or banner; Requestarr tries again on a later start");
            }
        }

        private static string Describe(BotImageAction action) => action switch
        {
            BotImageAction.UpToDate => "up to date",
            BotImageAction.Set => "setting Requestarr's",
            _ => "chosen by someone else, left as it is (botProfile.takeOver replaces it)"
        };
    }
}
