using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.Polish
{
    // Item 5: a title that already carries its year never shows it twice.
    [TestFixture]
    public class Item5YearFixture : PolishScenario
    {
        [TestCase("Dark Matter (2024)", 2024, "Dark Matter (2024)")]
        [TestCase("Dark Matter", 2015, "Dark Matter (2015)")]
        [TestCase("Dune", null, "Dune")]
        [TestCase("1917", 2019, "1917 (2019)")]
        [TestCase("Show (2020) ", 2020, "Show (2020) ")]
        public void with_year_adds_the_year_once(string title, int? year, string expected)
        {
            Titles.WithYear(title, year).Should().Be(expected);
        }

        [Test]
        public async Task the_year_appears_once_in_results_card_request_queue_and_approval()
        {
            var results = (await Harness.SendAsync(Search(Alice, "tv", "dark matter"))).LastReply;
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;
            var final = (await Click(Alice, $"req-tv-all:{DarkMatter}")).LastReply;
            var queue = (await Admin("queue")).LastReply;

            var shown = new[]
            {
                results.SelectMenu.Options.First().Label,
                card.Embeds.Single().Title,
                final.Content,
                final.Embeds.Single().Title,
                LastRequest.Label,
                LastAdminMessage.Content,
                LastAdminMessage.Embeds.Single().Title,
                queue.Content,
                DiscordText.RequestLine(LastRequest, "alice", true)
            };

            shown.Should().OnlyContain(s => s.Contains("Dark Matter (2024)") && !s.Contains("(2024) (2024)"));
        }
    }
}
