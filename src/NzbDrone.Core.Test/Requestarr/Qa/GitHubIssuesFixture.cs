using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using Requestarr.Core.Qa;

namespace Requestarr.Core.Test.Qa
{
    // Filing on GitHub: each label is made sure of first (an existing one is fine), then the issue, all with the
    // install's own key; a refusal gives no issue and never throws.
    [TestFixture]
    public class GitHubIssuesFixture
    {
        [Test]
        public async Task labels_first_then_the_issue_with_the_installs_own_key()
        {
            var sent = new List<(string Path, string Auth, string Body)>();
            var github = new GitHubIssues(LogManager.GetLogger("test"))
            {
                Transport = async (request, token) =>
                {
                    sent.Add((request.RequestUri.AbsolutePath, request.Headers.Authorization?.ToString(), await request.Content.ReadAsStringAsync(token)));
                    return request.RequestUri.AbsolutePath.EndsWith("/labels")
                        ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                        : new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"number\":12,\"html_url\":\"https://github.com/o/r/issues/12\"}") };
                }
            };

            var issue = await github.CreateAsync("o/r", "key", "Title", "Body", new[] { "bug report", "env: qa" }, CancellationToken.None);

            issue.Should().Be(new FiledIssue(12, "https://github.com/o/r/issues/12"));
            sent.Select(s => s.Path).Should().Equal("/repos/o/r/labels", "/repos/o/r/labels", "/repos/o/r/issues");
            sent.Should().OnlyContain(s => s.Auth == "Bearer key");
            sent.Last().Body.Should().Contain("\"labels\":[\"bug report\",\"env: qa\"]").And.Contain("\"title\":\"Title\"");
        }

        [Test]
        public async Task a_refusal_gives_no_issue()
        {
            var github = new GitHubIssues(LogManager.GetLogger("test")) { Transport = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)) };

            (await github.CreateAsync("o/r", "key", "T", "B", new[] { "x" }, CancellationToken.None)).Should().BeNull();
        }
    }
}
