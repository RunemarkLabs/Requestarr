using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Test.AnimeDub;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e item 9: the web app shows Requestarr's logo, in the set for the environment it runs in (dev, QA, beta for
    // staging, production for anything else) and the theme it shows (light or dark, Auto with the system setting).
    [TestFixture]
    public class Item9WebIconsFixture
    {
        private static readonly string[] Sets = { "production", "dev", "qa", "beta" };

        private static readonly string[] Files =
        {
            "android-chrome-192x192.png", "android-chrome-512x512.png", "apple-touch-icon.png", "favicon-16x16.png", "favicon-32x32.png", "favicon.ico",
            "logo.png", "logo-header.png", "mstile-144x144.png", "mstile-150x150.png", "mstile-310x150.png", "mstile-310x310.png", "mstile-70x70.png"
        };

        private static string Icons(params string[] parts) => Item4To6Fixture.RepoFile(new[] { "frontend", "src", "Content", "Images", "Icons" }.Concat(parts).ToArray());

        [TestCase("dev", "dev")]
        [TestCase("qa", "qa")]
        [TestCase(" QA ", "qa")]
        [TestCase("staging", "beta")]
        [TestCase(null, "production")]
        [TestCase("", "production")]
        [TestCase("anything else", "production")]
        public void the_set_follows_the_environment_it_runs_in(string environment, string set)
        {
            ReleaseEnvironment.IconSetFor(environment).Should().Be(set);
            new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.EnvironmentVariable ? environment : null }.IconSet.Should().Be(set);
        }

        [Test]
        public void every_environment_has_a_full_light_and_dark_set()
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                foreach (var set in Sets)
                {
                    Directory.GetFiles(Icons(theme, set)).Select(Path.GetFileName).Should().BeEquivalentTo(Files, $"{theme}/{set}");
                }
            }

            // The light theme's header is purple, so it has its own header logos (beta's bar is white there).
            foreach (var set in Sets)
            {
                Directory.GetFiles(Icons("light-purple", set)).Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "logo-header.png" }, $"light-purple/{set}");
            }

            File.Exists(Icons("safari-pinned-tab.svg")).Should().BeTrue();
        }

        [Test]
        public void the_pages_and_the_manifests_use_the_themed_sets_and_nothing_old_is_left()
        {
            var index = File.ReadAllText(Item4To6Fixture.RepoFile("frontend", "src", "index.ejs"));
            var login = File.ReadAllText(Item4To6Fixture.RepoFile("frontend", "src", "login.html"));

            foreach (var page in new[] { index, login })
            {
                page.Should().Contain("/Content/Images/Icons/__ICON_THEME__/__ICON_SET__/favicon-32x32.png")
                    .And.Contain("/Content/manifest-__ICON_THEME__.json")
                    .And.Contain("browserconfig-__ICON_THEME__.xml")
                    .And.Contain("'__UI_THEME__'")
                    .And.Contain("prefers-color-scheme: dark")
                    .And.NotContain("#7048e8", "the placeholder purple is gone");
            }

            // The sign-in panel header is dark gray in both themes, so its logo is always the dark set's.
            login.Should().Contain("src=\"/Content/Images/Icons/dark/__ICON_SET__/__HEADER_LOGO__\"")
                 .And.NotContain("Icons/__ICON_THEME__/__ICON_SET__/__HEADER_LOGO__");

            // While the app loads it shows the header's logo; nothing of the old placeholder is left.
            var loading = File.ReadAllText(Item4To6Fixture.RepoFile("frontend", "src", "Components", "Page", "LoadingPage.js"));
            loading.Should().Contain("<HeaderLogo").And.NotContain("data:image");

            foreach (var theme in new[] { "light", "dark" })
            {
                File.ReadAllText(Item4To6Fixture.RepoFile("frontend", "src", "Content", $"manifest-{theme}.json")).Should().Contain($"/Content/Images/Icons/{theme}/__ICON_SET__/android-chrome-512x512.png");
                File.ReadAllText(Item4To6Fixture.RepoFile("frontend", "src", "Content", $"browserconfig-{theme}.xml")).Should().Contain($"/Content/Images/Icons/{theme}/__ICON_SET__/mstile-150x150.png");
            }

            Directory.GetFiles(Icons()).Select(Path.GetFileName).Should().NotContain(f => f.StartsWith("favicon") || f.StartsWith("mstile") || f.StartsWith("android-chrome") || f.StartsWith("apple-touch"), "the placeholder icons and the debug favicon are removed");
            File.Exists(Item4To6Fixture.RepoFile("frontend", "src", "Content", "Images", "logo.png")).Should().BeFalse();
            File.Exists(Item4To6Fixture.RepoFile("frontend", "src", "Content", "Images", "logo.svg")).Should().BeFalse();
            File.ReadAllText(Item4To6Fixture.RepoFile("src", "Requestarr.Http", "Frontend", "Mappers", "FaviconMapper.cs")).Should().NotContain("favicon-debug");
        }
    }
}
