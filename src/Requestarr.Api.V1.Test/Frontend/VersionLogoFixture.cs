using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using Requestarr.Http.Frontend.Mappers;

namespace Requestarr.Api.V1.Test.Frontend
{
    // The production header and login logo: the running version on a green bar, laid out like the branch logos.
    [TestFixture]
    public class VersionLogoFixture
    {
        private static double Px(double units, double size) => units * size / VersionLogoGlyphs.UnitsPerEm;

        [Test]
        public void the_lettering_is_the_branch_labels_size_and_matches_the_delivered_logos()
        {
            var size = VersionLogo.FontSize("v0.4.9");

            size.Should().Be(117);

            // Measured from the delivered beta logo: 85 px across the bar and 299 px along it.
            var beta = VersionLogo.Ink("BETA");
            Px(beta.YMax - beta.YMin, size).Should().BeApproximately(85, 1);
            Px(beta.XMax - beta.XMin, size).Should().BeApproximately(299, 2);
        }

        [TestCase("v0.4.9")]
        [TestCase("v1.12.10")]
        public void a_usual_version_keeps_the_branch_size(string text)
        {
            VersionLogo.FontSize(text).Should().Be(117);
            Px(VersionLogo.Ink(text).XMax - VersionLogo.Ink(text).XMin, 117).Should().BeLessThan(0.86 * VersionLogo.Height);
        }

        [Test]
        public void a_long_version_shrinks_to_fit_the_bar_instead_of_being_cut_off()
        {
            var ink = VersionLogo.Ink("v10.100.1000");
            var size = VersionLogo.FontSize("v10.100.1000");

            size.Should().BeLessThan(117);
            Px(ink.XMax - ink.XMin, size).Should().BeApproximately(0.86 * VersionLogo.Height, 0.01);
        }

        [Test]
        public void the_text_is_the_version_to_three_parts()
        {
            VersionLogo.Text(new Version(0, 4, 9, 30588)).Should().Be("v0.4.9");
            VersionLogo.Text(new Version(1, 12, 10)).Should().Be("v1.12.10");
            VersionLogo.Text(new Version(2, 0)).Should().Be("v2.0");
        }

        [Test]
        public void the_logo_is_the_bar_the_version_and_the_delivered_r_with_the_page_showing_through()
        {
            var r = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
            var svg = VersionLogo.Svg(new Version(0, 4, 9, 30588), r);

            svg.Should().StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"662\" height=\"512\" viewBox=\"0 0 662 512\">")
               .And.Contain("<title>v0.4.9</title>")
               .And.Contain("<rect width=\"138\" height=\"512\" fill=\"#2EA043\"/>")
               .And.Contain($"<image x=\"176\" y=\"0\" width=\"486\" height=\"512\" href=\"data:image/png;base64,{Convert.ToBase64String(r)}\"/>");
            Regex.Matches(svg, "<path ").Count.Should().Be("v0.4.9".Length);
            Regex.Matches(svg, "<rect ").Count.Should().Be(1, "only the bar is drawn; there's no background behind the R or the gap");
        }

        [Test]
        public void the_version_reads_bottom_to_top_centred_in_the_bar()
        {
            var svg = VersionLogo.Svg(new Version(1, 12, 10), new byte[1]);
            var m = Regex.Match(svg, @"matrix\(0 (\S+) (\S+) 0 (\S+) (\S+)\)");
            var n = Enumerable.Range(1, 4).Select(i => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
            var ink = VersionLogo.Ink("v1.12.10");

            // The text's direction (font x) points up the bar and the letters' tops (font y) face left.
            n[0].Should().BeNegative();
            n[1].Should().BeNegative();

            // The middle of the ink lands in the middle of the bar.
            ((n[1] * (ink.YMin + ink.YMax) / 2) + n[2]).Should().BeApproximately(VersionLogo.BarWidth / 2.0, 0.01);
            ((n[0] * (ink.XMin + ink.XMax) / 2) + n[3]).Should().BeApproximately(VersionLogo.Height / 2.0, 0.01);
        }

        [Test]
        public void production_uses_the_drawn_logo_and_the_others_their_delivered_one()
        {
            WebIcons.HeaderLogo("production").Should().Be("logo-version.svg");

            foreach (var set in new[] { "dev", "qa", "beta" })
            {
                WebIcons.HeaderLogo(set).Should().Be("logo-header.png");
            }
        }

        [TestCase("/Content/Images/Icons/light/production/logo-version.svg", true)]
        [TestCase("/content/images/icons/dark/production/logo-version.svg", true)]
        [TestCase("/Content/Images/Icons/light-purple/production/logo-version.svg", true)]
        [TestCase("/Content/Images/Icons/dark/dev/logo-version.svg", false)]
        [TestCase("/Content/Images/Icons/dark/production/logo-header.png", false)]
        public void only_the_version_logo_is_drawn_and_everything_else_is_a_file(string url, bool drawn)
        {
            VersionLogoMapper.Handles(url).Should().Be(drawn);
            new StaticResourceMapper(null, null, null, null).CanHandle(url).Should().Be(!drawn);
        }

        [Test]
        public void it_is_drawn_with_the_running_version_beside_the_delivered_r()
        {
            var folder = Path.Combine(Path.GetTempPath(), "requestarr-version-logo-" + Guid.NewGuid().ToString("N"));
            var r = Path.Combine(folder, "UI", "Content", "Images", "Icons", "light-purple", "production", "logo-header.png");
            Directory.CreateDirectory(Path.GetDirectoryName(r)!);
            File.WriteAllBytes(r, new byte[] { 137, 80, 78, 71, 9, 9 });

            try
            {
                var appFolder = new Mock<IAppFolderInfo>();
                appFolder.SetupGet(a => a.StartUpFolder).Returns(folder);
                var config = new Mock<IConfigFileProvider>();
                config.SetupGet(c => c.UiFolder).Returns("UI");
                var disk = new Mock<IDiskProvider>();
                disk.Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);

                var mapper = new VersionLogoMapper(appFolder.Object, disk.Object, config.Object, LogManager.GetCurrentClassLogger());
                var result = (ContentResult)mapper.GetResponse("/Content/Images/Icons/light-purple/production/logo-version.svg").Result;

                result.ContentType.Should().Be("image/svg+xml");
                result.Content.Should().Contain($"<title>{VersionLogo.Text(BuildInfo.Version)}</title>")
                      .And.Contain(Convert.ToBase64String(File.ReadAllBytes(r)));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
