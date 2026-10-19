using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Requests
{
    // Item 6: approved requests go to the instance with its profile, root folder, series type; ID recorded.
    [TestFixture]
    public class Item6SendFixture : RequestScenario
    {
        [SetUp]
        public async Task AutoApprove()
        {
            await SetAccess(Alice, "movie", "auto");
            await SetAccess(Alice, "tv", "auto");
        }

        [Test]
        public async Task movie_should_use_instance_profile_and_root_and_record_item_id()
        {
            await RequestMovie(Alice, Dune);

            var add = Radarr.Adds.Single();
            add.Url.Should().Be("http://radarr-test:7878");
            add.QualityProfileId.Should().Be(4);
            add.RootFolder.Should().Be("/data/media");

            LastRequest.ExternalItemId.Should().Be(101);
            LastRequest.QualityProfileId.Should().Be(4);
            LastRequest.RootFolderPath.Should().Be("/data/media");
            LastRequest.BackendId.Should().Be(Instance("Radarr").Id);
        }

        [Test]
        public async Task series_should_use_instance_series_type()
        {
            await RequestTv(Alice, Severance, "all");

            Sonarr.Adds.Single().SeriesType.Should().Be("anime");
            Sonarr.Adds.Single().QualityProfileId.Should().Be(4);
            LastRequest.ExternalItemId.Should().Be(101);
        }

        [Test]
        public async Task backend_failure_should_mark_request_failed_and_tell_user()
        {
            Radarr.FailAdds = true;

            var run = await RequestMovie(Alice, Dune);

            LastRequest.Status.Should().Be(RequestStatus.Failed);
            LastRequest.DecisionReason.Should().Contain("validation failed");
            run.LastReply.Content.Should().Contain("Request #1 for Dune (2021) couldn't be added");
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public async Task admin_can_request_from_admin_channel_without_being_added()
        {
            var run = await Harness.SendAsync(FakeInteraction.Button($"req-make:movie:{DuneTwo}").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Contain(DiscordText.RequestSent("Dune: Part Two (2024)", 1, "Radarr"));
            UserFor(FakeDiscordGuild.Owner).PrivateChannelId.Should().BeNull();
            LastRequest.OwnerUserId.Should().Be(UserFor(FakeDiscordGuild.Owner).Id);
        }

        [Test]
        public async Task admin_search_in_admin_channel_uses_default_instance()
        {
            var run = await Harness.SendAsync(AdminRequest("movie", "dune"));

            run.LastReply.SelectMenu.Options.Should().HaveCount(2);
        }

        [Test]
        public async Task admin_record_does_not_block_adding_the_owner_as_a_user_later()
        {
            await Harness.SendAsync(FakeInteraction.Button($"req-make:movie:{DuneTwo}").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            (await AddUser(FakeDiscordGuild.Owner)).LastReply.Content.Should().StartWith("Added owner.");
        }
    }
}
