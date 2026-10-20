using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Converters;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;

namespace Requestarr.Core.Test.AnimeDub
{
    // Items 8 and 9: secrets never reach test or CI output; the build scans each change for secrets.
    [TestFixture]
    public class Item8And9SecretsFixture
    {
        private const string ApiKey = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

        // As the running app has it: backend settings are stored (and traced) as JSON.
        [SetUp]
        public void MapSettingsLikeTheApp()
        {
            SqlMapper.AddTypeHandler(typeof(BackendSettings), new ProviderSettingConverter());
        }

        [Test]
        public void the_sql_trace_never_prints_backend_api_keys()
        {
            var backend = new BackendInstance { Name = "Sonarr", Settings = new BackendSettings { Url = "http://sonarr:8989", ApiKey = ApiKey } };

            var trace = SqlBuilderExtensions.GetSqlLogString("INSERT INTO Backends (\"Settings\") VALUES (@Settings)", backend);

            trace.Should().Contain("==== Begin Query Trace ====").And.Contain("http://sonarr:8989");
            trace.Should().NotContain(ApiKey);
            trace.Should().Contain("(removed)");
        }

        [Test]
        public void the_sql_trace_never_prints_tokens()
        {
            var settings = new { Settings = "{\"botToken\": \"MTIzNDU2.Ghost.Token-Value-123456789\", \"accessToken\": \"secret-access\"}" };

            var trace = SqlBuilderExtensions.GetSqlLogString("UPDATE Config SET Value = @Settings", settings);

            trace.Should().NotContain("MTIzNDU2.Ghost.Token-Value-123456789").And.NotContain("secret-access");
        }

        [TestCase("\"botToken\": \"abc.def.ghi\"")]
        [TestCase("\"apiKey\": \"0123456789abcdef\"")]
        [TestCase("\"clientSecret\":\"hush\"")]
        public void the_log_cleanser_removes_secret_json_fields(string json)
        {
            CleanseLogMessage.Cleanse(json).Should().Contain("(removed)");
        }

        [Test]
        public void the_build_scans_every_change_for_secrets_with_a_pinned_self_tested_scanner()
        {
            var workflow = File.ReadAllText(Item4To6Fixture.RepoFile(".github", "workflows", "build.yml"));

            workflow.Should().Contain("  secrets:").And.Contain("name: Secret scan");
            Regex.IsMatch(workflow, @"gitleaks:v\d+\.\d+\.\d+").Should().BeTrue("the scanner image is pinned to a version");
            workflow.Should().Contain("Scanner self-test").And.Contain("--redact");
            Regex.Match(workflow, @"needs: \[[^\]]*\]").Value.Should().Contain("secrets", "the required build check fails when the scan fails");
        }

        [Test]
        public void scanner_allowances_are_exact_throwaway_values_only()
        {
            var config = File.ReadAllText(Item4To6Fixture.RepoFile(".gitleaks.toml"));
            var allowed = Regex.Matches(config, @"'''(?<rx>[^']+)'''").Select(m => m.Groups["rx"].Value).ToList();

            allowed.Should().ContainSingle();
            allowed.Should().OnlyContain(rx => !rx.Contains(".*") && !rx.Contains(@"\w") && !rx.Contains('+') && !rx.Contains('['), "allowances must be exact values, never patterns");
            File.ReadAllText(Item4To6Fixture.RepoFile("src", "NzbDrone.Core.Test", "Requestarr", "Polish", "Item4CastFixture.cs"))
                .Should().Contain(allowed[0].Replace(@"\.", "."), "the one allowance is the fake token that test uses");
        }

        [Test]
        public void the_secrets_file_stays_out_of_git()
        {
            File.ReadAllText(Item4To6Fixture.RepoFile(".gitignore")).Should().Contain("secrets.env");
            DiscordText.TmdbNotice.Should().NotBeNull();
        }
    }
}
