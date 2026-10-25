using System;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Access
{
    // Access requests from the welcome channel: how long someone waits after a denial before asking again, and the
    // spam guard's numbers. Kept in Requestarr's settings (the admin web portal reads and changes them there);
    // provisioning sets the ones it names on every start.
    public interface IAccessSettings
    {
        int DenialCooldownDays { get; }

        // Counted clicks are those within the last SpamWindowMinutes. ShortCooldownClicks of them: a cooldown of
        // ShortCooldownMinutes. LongCooldownClicks: LongCooldownHours. BlockClicks more within BlockWithinHours of
        // that: blocked.
        int SpamWindowMinutes { get; }
        int ShortCooldownClicks { get; }
        int ShortCooldownMinutes { get; }
        int LongCooldownClicks { get; }
        int LongCooldownHours { get; }
        int BlockClicks { get; }
        int BlockWithinHours { get; }

        void Apply(AccessRequestsProvisioning provisioning);
    }

    public class AccessSettings : IAccessSettings
    {
        public const int DefaultDenialCooldownDays = 7;
        public const int DefaultSpamWindowMinutes = 60;
        public const int DefaultShortCooldownClicks = 5;
        public const int DefaultShortCooldownMinutes = 1;
        public const int DefaultLongCooldownClicks = 10;
        public const int DefaultLongCooldownHours = 24;
        public const int DefaultBlockClicks = 5;
        public const int DefaultBlockWithinHours = 24;

        private readonly IConfigService _config;

        public AccessSettings(IConfigService config)
        {
            _config = config;
        }

        public int DenialCooldownDays => Math.Max(0, _config.AccessDenialCooldownDays);
        public int SpamWindowMinutes => AtLeastOne(_config.AccessSpamWindowMinutes, DefaultSpamWindowMinutes);
        public int ShortCooldownClicks => AtLeastOne(_config.AccessShortCooldownClicks, DefaultShortCooldownClicks);
        public int ShortCooldownMinutes => AtLeastOne(_config.AccessShortCooldownMinutes, DefaultShortCooldownMinutes);
        public int LongCooldownClicks => AtLeastOne(_config.AccessLongCooldownClicks, DefaultLongCooldownClicks);
        public int LongCooldownHours => AtLeastOne(_config.AccessLongCooldownHours, DefaultLongCooldownHours);
        public int BlockClicks => AtLeastOne(_config.AccessBlockClicks, DefaultBlockClicks);
        public int BlockWithinHours => AtLeastOne(_config.AccessBlockWithinHours, DefaultBlockWithinHours);

        public void Apply(AccessRequestsProvisioning provisioning)
        {
            if (provisioning == null)
            {
                return;
            }

            if (provisioning.DenialCooldownDays is { } days)
            {
                _config.AccessDenialCooldownDays = Math.Max(0, days);
            }

            var spam = provisioning.Spam;

            if (spam == null)
            {
                return;
            }

            Set(spam.WindowMinutes, v => _config.AccessSpamWindowMinutes = v);
            Set(spam.ShortCooldownClicks, v => _config.AccessShortCooldownClicks = v);
            Set(spam.ShortCooldownMinutes, v => _config.AccessShortCooldownMinutes = v);
            Set(spam.LongCooldownClicks, v => _config.AccessLongCooldownClicks = v);
            Set(spam.LongCooldownHours, v => _config.AccessLongCooldownHours = v);
            Set(spam.BlockClicks, v => _config.AccessBlockClicks = v);
            Set(spam.BlockWithinHours, v => _config.AccessBlockWithinHours = v);
        }

        private static void Set(int? value, Action<int> set)
        {
            if (value is { } v)
            {
                set(Math.Max(1, v));
            }
        }

        private static int AtLeastOne(int value, int fallback) => value >= 1 ? value : fallback;
    }
}
