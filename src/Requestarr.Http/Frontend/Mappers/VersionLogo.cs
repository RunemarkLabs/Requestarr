using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Requestarr.Http.Frontend.Mappers
{
    // The production header and login logo: a green bar with the running version beside the delivered R
    // (Icons/{theme}/production/logo-header.png), laid out like the dev, QA and beta logos so it always shows the
    // version actually running. The rules are the ones the branch logos were made with, at 512 px tall: a 138 px bar,
    // a 38 px gap, white Inter Display Black reading bottom to top and centred in the bar, at the largest size where
    // "BETA" is no taller than 62% of the bar's width and no longer than 80% of the height; the version uses that size
    // and shrinks only if it would be longer than 86% of the height.
    public static class VersionLogo
    {
        public const int Height = 512;
        public const int BarWidth = 138;
        public const int LogoX = 176;
        public const int LogoWidth = 486;
        public const int Width = LogoX + LogoWidth;
        public const string Green = "#2EA043";

        public static string Text(Version version) => "v" + version.ToString(version.Build >= 0 ? 3 : 2);

        // The font size in pixels: the branch labels' size, smaller only when the text would be too long.
        public static double FontSize(string text)
        {
            var beta = Ink("BETA");
            var size = Math.Floor(Math.Min(0.62 * BarWidth * VersionLogoGlyphs.UnitsPerEm / (beta.YMax - beta.YMin),
                                           0.80 * Height * VersionLogoGlyphs.UnitsPerEm / (beta.XMax - beta.XMin)));
            var ink = Ink(text);
            var longest = 0.86 * Height * VersionLogoGlyphs.UnitsPerEm / (ink.XMax - ink.XMin);

            return Math.Min(size, longest);
        }

        public static string Svg(Version version, byte[] logo, bool dark)
        {
            var text = Text(version);
            var ink = Ink(text);
            var scale = FontSize(text) / VersionLogoGlyphs.UnitsPerEm;

            // Font units (y up) to the bar, turned to read bottom to top with the ink centred on the bar.
            var e = (BarWidth / 2.0) + (scale * (ink.YMin + ink.YMax) / 2);
            var f = (Height / 2.0) + (scale * (ink.XMin + ink.XMax) / 2);

            var svg = new StringBuilder();
            svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\">");
            svg.Append($"<title>{text}</title>");
            svg.Append($"<rect width=\"{Width}\" height=\"{Height}\" fill=\"{(dark ? "#282828" : "#FFFFFF")}\"/>");
            svg.Append($"<rect width=\"{BarWidth}\" height=\"{Height}\" fill=\"{Green}\"/>");
            svg.Append($"<g fill=\"#FFFFFF\" transform=\"matrix(0 {N(-scale)} {N(-scale)} 0 {N(e)} {N(f)})\">");

            foreach (var (letter, x) in Layout(text))
            {
                svg.Append($"<path transform=\"translate({N(x)} 0)\" d=\"{VersionLogoGlyphs.Letters[letter].Path}\"/>");
            }

            svg.Append("</g>");
            svg.Append($"<image x=\"{LogoX}\" y=\"0\" width=\"{LogoWidth}\" height=\"{Height}\" href=\"data:image/png;base64,{Convert.ToBase64String(logo)}\"/>");
            svg.Append("</svg>");

            return svg.ToString();
        }

        // Where the text's ink sits, in font units from the start of the first letter.
        public static (double XMin, double YMin, double XMax, double YMax) Ink(string text)
        {
            var letters = Layout(text).Select(l => (Glyph: VersionLogoGlyphs.Letters[l.Letter], l.X)).ToList();

            return (letters.Min(l => l.X + l.Glyph.XMin), letters.Min(l => l.Glyph.YMin),
                    letters.Max(l => l.X + l.Glyph.XMax), letters.Max(l => l.Glyph.YMax));
        }

        private static (char Letter, double X)[] Layout(string text)
        {
            var x = 0.0;

            return text.Select((letter, i) =>
            {
                var at = x;
                var next = i + 1 < text.Length ? text.Substring(i, 2) : null;
                x += VersionLogoGlyphs.Letters[letter].Advance + (next != null && VersionLogoGlyphs.Kerning.TryGetValue(next, out var kern) ? kern : 0);

                return (letter, at);
            }).ToArray();
        }

        private static string N(double value) => Math.Round(value, 6).ToString(CultureInfo.InvariantCulture);
    }
}
