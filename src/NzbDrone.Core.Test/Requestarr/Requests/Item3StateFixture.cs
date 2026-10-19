using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Requests
{
    // Item 3: state shown before requesting: available, already requested (subscribe), previously denied.
    [TestFixture]
    public class Item3StateFixture : RequestScenario
    {
        private Task<InteractionRun> Detail(ulong user, int tmdbId) =>
            Harness.SendAsync(UserCommand("request movie-tmdb", user).WithOption("id", (long)tmdbId));

        [Test]
        public async Task title_in_library_should_show_available_without_request_button()
        {
            var card = (await Detail(Alice, Arrival)).LastReply;

            card.Content.Should().Be(DiscordText.StateInLibrary);
            card.Buttons.Should().BeEmpty();
            (await RequestMovie(Alice, Arrival)).LastReply.Content.Should().Be(DiscordText.AlreadyInLibrary);
            RequestStore.All().Should().BeEmpty();
        }

        [Test]
        public async Task duplicate_request_should_attach_subscriber_not_create_second_request()
        {
            await RequestMovie(Alice, Dune);

            var card = (await Detail(Bob, Dune)).LastReply;
            card.Content.Should().Be(DiscordText.StateRequested("alice", RequestStatus.Pending, 1));
            card.Buttons.Single().Label.Should().Be(DiscordText.FollowLabel);

            var run = await RequestMovie(Bob, Dune);

            run.LastReply.Content.Should().Be(DiscordText.Subscribed("Dune (2021)", "alice", 1));
            RequestStore.All().Should().ContainSingle();
            Mocker.Resolve<IRequestSubscriberRepository>().ForRequest(1).Single().UserId.Should().Be(UserFor(Bob).Id);
        }

        [Test]
        public async Task owner_and_existing_subscriber_should_be_told_instead_of_duplicating()
        {
            await RequestMovie(Alice, Dune);
            await RequestMovie(Bob, Dune);

            (await RequestMovie(Alice, Dune)).LastReply.Content.Should().Be(DiscordText.AlreadyRequestedByYou);
            (await RequestMovie(Bob, Dune)).LastReply.Content.Should().Be(DiscordText.AlreadyFollowing);
            (await Detail(Alice, Dune)).LastReply.Content.Should().Be(DiscordText.StateYours(RequestStatus.Pending, 1));
            (await Detail(Bob, Dune)).LastReply.Content.Should().Be(DiscordText.StateFollowing(1));
            Mocker.Resolve<IRequestSubscriberRepository>().All().Should().ContainSingle();
        }

        [Test]
        public async Task previously_denied_should_show_reason_and_allow_new_request()
        {
            await RequestMovie(Alice, Dune);
            await Harness.SendAsync(AdminSlash("deny").WithOption("id", 1L).WithOption("reason", "not in English"));

            var card = (await Detail(Bob, Dune)).LastReply;

            card.Content.Should().Be(DiscordText.StatePreviouslyDenied("not in English"));
            card.Buttons.Single().Label.Should().Be(DiscordText.RequestLabel);

            (await RequestMovie(Bob, Dune)).LastReply.Content.Should().Be(DiscordText.RequestPending("Dune (2021)", 2));
        }

        [Test]
        public async Task requests_on_different_instances_are_separate()
        {
            await SetAccess(Bob, "movie", "approval", "Radarr 4K");
            await RequestMovie(Alice, Dune);

            (await RequestMovie(Bob, Dune)).LastReply.Content.Should().Be(DiscordText.RequestPending("Dune (2021)", 2));
            RequestStore.All().Select(r => r.BackendId).Distinct().Should().HaveCount(2);
        }
    }
}
