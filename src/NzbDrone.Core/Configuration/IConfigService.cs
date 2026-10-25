using System.Collections.Generic;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Configuration
{
    public interface IConfigService
    {
        void SaveConfigDictionary(Dictionary<string, object> configValues);

        bool IsDefined(string key);

        //History
        int HistoryCleanupDays { get; set; }

        // Requestarr removal votes (days); read through Requestarr.Core.Removals.RemovalSettings
        int RemovalVoteDays { get; set; }
        int RemovalRequesterTimeoutDays { get; set; }
        int RemovalReminderDay { get; set; }
        int AccessDenialCooldownDays { get; set; }
        int AccessSpamWindowMinutes { get; set; }
        int AccessShortCooldownClicks { get; set; }
        int AccessShortCooldownMinutes { get; set; }
        int AccessLongCooldownClicks { get; set; }
        int AccessLongCooldownHours { get; set; }
        int AccessBlockClicks { get; set; }
        int AccessBlockWithinHours { get; set; }

        // Requestarr's bot picture and banner; read through Requestarr.Core.Discord.BotProfileSettings
        bool BotProfileKeepUpToDate { get; set; }
        bool BotProfileTakeOver { get; set; }
        string BotProfileAvatarSet { get; set; }
        string BotProfileBannerSet { get; set; }

        //UI
        int FirstDayOfWeek { get; set; }
        string CalendarWeekColumnHeader { get; set; }

        string ShortDateFormat { get; set; }
        string LongDateFormat { get; set; }
        string TimeFormat { get; set; }
        bool ShowRelativeDates { get; set; }
        bool EnableColorImpairedMode { get; set; }
        string UILanguage { get; set; }

        //Internal
        string PlexClientIdentifier { get; }

        //Forms Auth
        string RijndaelPassphrase { get; }
        string HmacPassphrase { get; }
        string RijndaelSalt { get; }
        string HmacSalt { get; }

        //Link Protection
        string DownloadProtectionKey { get; }

        //Proxy
        bool ProxyEnabled { get; }
        ProxyType ProxyType { get; }
        string ProxyHostname { get; }
        int ProxyPort { get; }
        string ProxyUsername { get; }
        string ProxyPassword { get; }
        string ProxyBypassFilter { get; }
        bool ProxyBypassLocalAddresses { get; }

        // Backups
        string BackupFolder { get; }
        int BackupInterval { get; }
        int BackupRetention { get; }

        // Indexers
        bool LogIndexerResponse { get; set; }

        CertificateValidationType CertificateValidation { get; }
        string ApplicationUrl { get; }
    }
}
