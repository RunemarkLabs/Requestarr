using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Common.Test.ExtensionTests.StringExtensionTests
{
    // Text from a request stays on one log line, so it can't add a fake entry to the log.
    [TestFixture]
    public class ForLogFixture
    {
        [TestCase("/api/v1/system", "/api/v1/system")]
        [TestCase("/x\n2026-10-25 Info Auth-Success", "/x\\n2026-10-25 Info Auth-Success")]
        [TestCase("admin\r\nfake", "admin\\r\\nfake")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void should_keep_text_on_one_line(string input, string expected)
        {
            input.ForLog().Should().Be(expected);
        }
    }
}
