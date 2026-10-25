using System;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Http.Frontend.Mappers
{
    // Where the web app's icons and logo come from: Content/Images/Icons/{theme}/{set}/, with set the environment the
    // app runs in (IReleaseEnvironment.IconSet) and theme light or dark. Pages are served with the theme from the UI
    // settings (Auto starts light) and a small script in each page switches to the theme the app is showing, including
    // Auto following the system setting while the page is open.
    public static class WebIcons
    {
        public const string SetToken = "__ICON_SET__";
        public const string ThemeToken = "__ICON_THEME__";
        public const string UiThemeToken = "__UI_THEME__";

        public static string Theme(string configured) => string.Equals(configured, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";

        // The theme a themed file asks for in its name ("manifest-dark.json"), light otherwise.
        public static string ThemeOf(string resourceUrl) => resourceUrl.Contains("-dark.", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";

        public static string Apply(string text, IReleaseEnvironment environment, IConfigFileProvider config, string theme = null)
        {
            return text.Replace(SetToken, environment.IconSet)
                       .Replace(ThemeToken, theme ?? Theme(config.Theme))
                       .Replace(UiThemeToken, config.Theme);
        }
    }
}
