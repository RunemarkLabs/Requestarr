using System;
using Dapper;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class CleanupUnusedTagsFixture : DbTest<CleanupUnusedTags, Tag>
    {
        [Test]
        public void should_delete_unused_tags()
        {
            var tags = Builder<Tag>.CreateListOfSize(2)
                .All()
                .With(x => x.Id = 0)
                .BuildList();

            Db.InsertMany(tags);
            Subject.Clean();
            AllStoredModels.Should().BeEmpty();
        }

        [Test]
        public void should_not_delete_used_tags()
        {
            var tags = Builder<Tag>
                .CreateListOfSize(2)
                .All()
                .With(x => x.Id = 0)
                .BuildList();

            Db.InsertMany(tags);

            using (var connection = Mocker.Resolve<IMainDatabase>().OpenConnection())
            {
                connection.Execute("INSERT INTO \"Backends\" (\"Name\", \"Implementation\", \"Enable\", \"Tags\", \"Added\") VALUES (@Name, 'Sonarr', @Enable, @Tags, @Added)",
                    new { Name = "Sonarr", Enable = true, Tags = $"[{tags[0].Id}]", Added = DateTime.UtcNow });
            }

            Subject.Clean();
            AllStoredModels.Should().HaveCount(1);
        }
    }
}
