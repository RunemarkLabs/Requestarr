using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    [TestFixture]
    public class InitialSchemaFixture : DbTest
    {
        [TestCase("RequestarrUsers")]
        [TestCase("UserIdentities")]
        [TestCase("UserMediaAccess")]
        [TestCase("MediaServerAccounts")]
        [TestCase("MediaServerLibraryMappings")]
        [TestCase("MediaServerAccessRequests")]
        [TestCase("Requests")]
        [TestCase("RequestSubscribers")]
        [TestCase("RemovalProposals")]
        [TestCase("RemovalFlags")]
        [TestCase("AuditEntries")]
        [TestCase("DiscordGuildConfigs")]
        [TestCase("Backends")]
        public void core_table_should_exist(string table)
        {
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();

            connection.Query<int>($"SELECT COUNT(*) FROM \"{table}\"").Single().Should().Be(0);
        }

        [Test]
        public void user_identity_should_be_unique_per_provider_and_external_id()
        {
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            const string insert = "INSERT INTO \"UserIdentities\" (\"UserId\", \"Provider\", \"ExternalId\", \"Linked\") VALUES (@UserId, 'Discord', '123', '2026-10-19T00:00:00Z')";

            connection.Execute(insert, new { UserId = 1 });

            var act = () => connection.Execute(insert, new { UserId = 2 });

            act.Should().Throw<System.Exception>();
        }

        [Test]
        public void backends_should_allow_several_instances_of_one_type()
        {
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            const string insert = "INSERT INTO \"Backends\" (\"Name\", \"Implementation\", \"Enable\", \"Added\") VALUES (@Name, 'Radarr', 1, '2026-10-19T00:00:00Z')";

            connection.Execute(insert, new { Name = "Radarr" });
            connection.Execute(insert, new { Name = "Radarr 4K" });

            connection.Query<int>("SELECT COUNT(*) FROM \"Backends\" WHERE \"Implementation\" = 'Radarr'").Single().Should().Be(2);
        }
    }
}
