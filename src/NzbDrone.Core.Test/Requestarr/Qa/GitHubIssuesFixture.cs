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
    // install's own key; a refusal gives no issue, never throws, and says why in words an admin can act on.
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

            issue.Should().Be(new IssueResult(new FiledIssue(12, "https://github.com/o/r/issues/12"), null));
            sent.Select(s => s.Path).Should().Equal("/repos/o/r/labels", "/repos/o/r/labels", "/repos/o/r/issues");
            sent.Should().OnlyContain(s => s.Auth == "Bearer key");
            sent.Last().Body.Should().Contain("\"labels\":[\"bug report\",\"env: qa\"]").And.Contain("\"title\":\"Title\"");
        }

        [Test]
        public async Task a_refusal_gives_no_issue_and_says_what_github_meant()
        {
            var github = new GitHubIssues(LogManager.GetLogger("test")) { Transport = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)) };

            var result = await github.CreateAsync("o/r", "key", "T", "B", new[] { "x" }, CancellationToken.None);

            result.Issue.Should().BeNull();
            result.Problem.Should().Be("the access key isn't allowed to create issues on o/r (HTTP 403)");
            GitHubIssues.Refused(HttpStatusCode.Unauthorized, "o/r").Should().Contain("HTTP 401");
        }

        [Test]
        public async Task github_out_of_reach_gives_no_issue_and_says_so()
        {
            var github = new GitHubIssues(LogManager.GetLogger("test")) { Transport = (_, _) => throw new HttpRequestException("no route") };

            var result = await github.CreateAsync("o/r", "key", "T", "B", new[] { "x" }, CancellationToken.None);

            result.Should().Be(new IssueResult(null, "GitHub couldn't be reached (HttpRequestException)"));
        }
    }
}
