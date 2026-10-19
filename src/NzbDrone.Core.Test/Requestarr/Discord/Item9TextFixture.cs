using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord;

namespace Requestarr.Core.Test.Discord
{
    // Item 9: all user-facing Discord text lives in DiscordText.
    [TestFixture]
    public class Item9TextFixture : TestBase
    {
        private static readonly string SourceRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "src", "NzbDrone.Core", "Requestarr"));

        [Test]
        public void text_constants_should_be_non_empty_and_within_message_limit()
        {
            var constants = typeof(DiscordText).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Concat(typeof(DiscordText.Commands).GetFields(BindingFlags.Public | BindingFlags.Static))
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .ToList();

            constants.Should().HaveCountGreaterThan(30);
            constants.Should().OnlyContain(c => c.Length > 0 && c.Length <= DiscordLimits.MessageLength);
        }

        [Test]
        public void command_descriptions_should_fit_discord_limit()
        {
            typeof(DiscordText.Commands).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (string)f.GetRawConstantValue())
                .Should().OnlyContain(d => d.Length <= DiscordLimits.OptionLabelLength);
        }

        [Test]
        public void handlers_and_services_should_not_contain_user_facing_sentences()
        {
            if (!Directory.Exists(SourceRoot))
            {
                Assert.Ignore("Source tree not available next to test output");
            }

            // Any string literal that reads like a sentence sent to users must come from DiscordText.
            var sentence = new Regex("\"[A-Z][a-z]+( [a-z'][a-z']*){3,}[^\"]*[.!?]\"");
            var offenders = Directory.GetFiles(Path.Combine(SourceRoot, "Discord", "Handlers"), "*.cs")
                .Append(Path.Combine(SourceRoot, "Users", "UserManagementService.cs"))
                .SelectMany(file => File.ReadAllLines(file).Select((line, i) => (file, line, i)))
                .Where(x => sentence.IsMatch(x.line) && !x.line.TrimStart().StartsWith("//") && !x.line.Contains("_logger."))
                .Select(x => $"{Path.GetFileName(x.file)}:{x.i + 1}")
                .ToList();

            offenders.Should().BeEmpty();
        }
    }
}
