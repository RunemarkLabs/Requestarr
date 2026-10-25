using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Requestarr.Core.Qa
{
    public record FiledIssue(int Number, string Url);

    public interface IGitHubIssues
    {
        // The new issue, or null when GitHub refused or couldn't be reached (logged without the key).
        Task<FiledIssue> CreateAsync(string repository, string token, string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken);
    }

    // Files issues on GitHub with the install's own access key. Uses its own HttpClient so requests, which carry the key,
    // never reach the logs; only the HTTP status or the exception type is logged.
    public class GitHubIssues : IGitHubIssues
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly Uri BaseUri = new("https://api.github.com/");

        private readonly Logger _logger;

        public GitHubIssues(Logger logger)
        {
            _logger = logger;
        }

        // Swapped in tests; the live transport is the shared HttpClient.
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Transport { get; set; } = (request, token) => Http.SendAsync(request, token);

        public async Task<FiledIssue> CreateAsync(string repository, string token, string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken)
        {
            try
            {
                // A label GitHub doesn't know yet is created first, so issues can be filtered by environment, version
                // and component from the start. "Already exists" (422) is fine.
                foreach (var label in labels)
                {
                    using var create = Build(HttpMethod.Post, $"repos/{repository}/labels", token, new { name = label, color = LabelColor(label) });
                    using var created = await Transport(create, cancellationToken);

                    if (!created.IsSuccessStatusCode && created.StatusCode != HttpStatusCode.UnprocessableEntity)
                    {
                        _logger.Warn("GitHub refused to create the issue label '{0}' (HTTP {1})", label, (int)created.StatusCode);
                    }
                }

                using var request = Build(HttpMethod.Post, $"repos/{repository}/issues", token, new { title, body, labels });
                using var response = await Transport(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("GitHub refused the bug report issue (HTTP {0})", (int)response.StatusCode);
                    return null;
                }

                var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

                return new FiledIssue((int)json["number"], (string)json["html_url"]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.Warn("Filing the bug report on GitHub failed ({0})", ex.GetType().Name);
                return null;
            }
        }

        public static HttpRequestMessage Build(HttpMethod method, string path, string token, object payload)
        {
            var request = new HttpRequestMessage(method, new Uri(BaseUri, path))
            {
                Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            };

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Requestarr", "1"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            return request;
        }

        private static string LabelColor(string label) => label.Split(':')[0] switch
        {
            "env" => "5319e7",
            "version" => "0e8a16",
            "component" => "1d76db",
            _ => "d73a4a"
        };
    }
}
