using System.Collections.Generic;
using NzbDrone.Core.Configuration;
using Requestarr.Http.REST;

namespace Requestarr.Api.V1.Config
{
    public class UiConfigResource : RestResource
    {
        //Calendar
        public int FirstDayOfWeek { get; set; }
        public string CalendarWeekColumnHeader { get; set; }

        //Dates
        public string ShortDateFormat { get; set; }
        public string LongDateFormat { get; set; }
        public string TimeFormat { get; set; }
        public bool ShowRelativeDates { get; set; }

        public bool EnableColorImpairedMode { get; set; }
        public string UILanguage { get; set; }
        public string Theme { get; set; }

        // Requestarr: the settings the provisioning file sets (theme can be one), shown read-only (never saved).
        public IReadOnlyCollection<string> Provisioned { get; set; }
    }

    public static class UiConfigResourceMapper
    {
        public static UiConfigResource ToResource(IConfigFileProvider config, IConfigService model)
        {
            return new UiConfigResource
            {
                FirstDayOfWeek = model.FirstDayOfWeek,
                CalendarWeekColumnHeader = model.CalendarWeekColumnHeader,

                ShortDateFormat = model.ShortDateFormat,
                LongDateFormat = model.LongDateFormat,
                TimeFormat = model.TimeFormat,
                ShowRelativeDates = model.ShowRelativeDates,

                EnableColorImpairedMode = model.EnableColorImpairedMode,
                UILanguage = model.UILanguage,
                Theme = config.Theme
            };
        }
    }
}
