using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.AvailabilityFeed
{
    // Item 3: the owner, any follower or an admin adds seasons to a TV request at any time, with no
    // approval and no quota cost. Seasons are never removed here.
    [TestFixture]
    public class Item3AddSeasonsFixture : FeedScenario
    {
        private const int Series = 101;

        [Test]
        public async Task the_owner_adds_a_season_to_a_sent_request_from_its_message()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1);
            var message = ulong.Parse(Reload(request.Id).MessageId);
            Latest(Alice).Buttons.Select(b => b.CustomId).Should().Contain($"req-add:{request.Id}");

            var picker = (await ClickOn(Alice, message, $"req-add:{request.Id}")).LastReply;

            picker.Content.Should().Contain("Already requested: **season 1 only; new seasons won't be added**");
            picker.SelectMenu.Options.Select(o => o.Value).Should().Equal("s2", "s3");
            picker.Buttons.Single(b => b.Label == DiscordText.AddTheseSeasonsLabel).Disabled.Should().BeTrue();

            var picked = (await ChooseOn(Alice, message, picker.SelectMenu.CustomId, "s3")).LastReply;
            picked.Content.Should().Contain(DiscordText.PickedSoFar("season 3"));
            var done = (await ClickOn(Alice, message, picked.Buttons.Single(b => b.Label == DiscordText.AddTheseSeasonsLabel).CustomId)).LastReply;

            var added = Sonarr.SeasonAdds.Should().ContainSingle().Which;
            (added.SeriesId, added.Future).Should().Be((Series, false));
            added.Seasons.Should().Equal(3);
            Reload(request.Id).Seasons.Should().Equal(1, 3);
            Reload(request.Id).Status.Should().Be(RequestStatus.Approved);
            done.Content.Should().Contain("Seasons: seasons 1 and 3 only").And.Contain(DiscordText.SeasonsAddedNote("season 3"));
            ChannelFor(Alice).MessageIds.Should().Contain(message, "the owner's own message changes in place");
            AuditTrail.Last().Action.Should().Be("request.seasons.add");
            Discord.Channels[AdminChannel].Messages.Should().NotContain(m => m.Content != null && m.Content.Contains("Approve"), "adding seasons needs no approval");
        }

        [Test]
        public async Task adding_seasons_costs_no_quota()
        {
            await Harness.SendAsync(AdminCommand("user quota", Alice).WithOption("type", "tv").WithOption("limit", 1L));
            var request = await SentShow(Alice, DarkMatter2015, 1);

            var result = await Requests.AddSeasonsAsync(request.Id, UserFor(Alice), false, Actor.System, new SeasonChoice(false, new[] { 2 }, false));

            result.Success.Should().BeTrue();
            Reload(request.Id).Seasons.Should().Equal(1, 2);
        }

        [Test]
        public async Task a_follower_adds_seasons_and_the_owner_hears_about_it()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1);
            var bobs = await Ask(Bob, "tv", $"tvdb:{DarkMatter2015}");
            await ClickOn(Bob, bobs, $"req-follow:tv:{DarkMatter2015}");
            var alicesBefore = ulong.Parse(Reload(request.Id).MessageId);

            var picker = (await ClickOn(Bob, bobs, $"req-add:{request.Id}")).LastReply;
            await ChooseOn(Bob, bobs, picker.SelectMenu.CustomId, "s2");
            var done = (await ClickOn(Bob, bobs, $"req-add-ok:{request.Id}:2")).LastReply;

            Reload(request.Id).Seasons.Should().Equal(1, 2);
            done.Content.Should().Contain("You're following alice's request");
            ChannelFor(Alice).MessageIds.Should().NotContain(alicesBefore, "the owner's message is reposted with the news");
            Latest(Alice).Content.Should().StartWith($"<@{Alice}>").And.Contain("Seasons: seasons 1–2 only");
        }

        [Test]
        public async Task an_admin_adds_seasons_from_the_feed_card()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1);
            var card = ulong.Parse(Reload(request.Id).FeedMessageId);

            var picker = (await FeedClick(request.Id, $"req-add:{request.Id}")).LastReply;
            picker.SelectMenu.Options.Select(o => o.Value).Should().Equal("s2", "s3");
            await FeedChoose(request.Id, picker.SelectMenu.CustomId, "s2", "s3");
            var done = await FeedClick(request.Id, $"req-add-ok:{request.Id}:2-3");

            done.LastReply.Ephemeral.Should().BeTrue();
            done.LastReply.Content.Should().Be(DiscordText.SeasonsAdded(Reload(request.Id), "seasons 2–3", false));
            Reload(request.Id).Seasons.Should().Equal(1, 2, 3);
            Feed.MessageIds.Should().NotContain(card);
            CardOf(request.Id).Embeds[0].Description.Should().Contain("seasons 1–3");
            Latest(Alice).Content.Should().StartWith($"<@{Alice}>").And.Contain("seasons 1–3");
        }

        [Test]
        public async Task seasons_added_while_pending_go_to_sonarr_with_the_approval()
        {
            await AskSeasons(Alice, DarkMatter2015, 1);

            var result = await Requests.AddSeasonsAsync(1, UserFor(Alice), false, Actor.System, new SeasonChoice(false, new[] { 3 }, false));

            result.Message.Should().Be(DiscordText.SeasonsAdded(Reload(1), "season 3", true));
            Sonarr.SeasonAdds.Should().BeEmpty();
            Reload(1).Status.Should().Be(RequestStatus.Pending);

            await AdminButton("req-approve:1");
            Sonarr.Adds.Single().Seasons.Should().Equal(1, 3);
        }

        [Test]
        public async Task new_seasons_as_they_air_can_be_added_but_never_turned_off_here()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1);
            var message = ulong.Parse(Reload(request.Id).MessageId);

            var picker = (await ClickOn(Alice, message, $"req-add:{request.Id}")).LastReply;
            var toggle = picker.Buttons.Single(b => b.Label == DiscordText.FutureToggle(false));
            var on = (await ClickOn(Alice, message, toggle.CustomId)).LastReply;
            await ClickOn(Alice, message, on.Buttons.Single(b => b.Label == DiscordText.AddTheseSeasonsLabel).CustomId);

            Reload(request.Id).MonitorFutureSeasons.Should().BeTrue();
            Sonarr.SeasonAdds.Single().Future.Should().BeTrue();

            var again = (await ClickOn(Alice, ulong.Parse(Reload(request.Id).MessageId), $"req-add:{request.Id}")).LastReply;
            again.Buttons.Should().NotContain(b => b.Label == DiscordText.FutureToggle(true) || b.Label == DiscordText.FutureToggle(false));
        }

        [Test]
        public async Task seasons_already_requested_are_never_offered_or_removed()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1, 2);

            var result = await Requests.AddSeasonsAsync(request.Id, UserFor(Alice), false, Actor.System, new SeasonChoice(false, new[] { 1 }, false));

            result.Success.Should().BeFalse();
            result.Message.Should().Be(DiscordText.NothingNewToAdd);
            Reload(request.Id).Seasons.Should().Equal(1, 2);

            await Requests.AddSeasonsAsync(request.Id, UserFor(Alice), false, Actor.System, new SeasonChoice(false, new[] { 3 }, false));
            Reload(request.Id).Seasons.Should().Equal(new[] { 1, 2, 3 }, "adding keeps what was there");
        }

        [Test]
        public async Task when_every_season_is_requested_the_picker_says_so()
        {
            var request = await SentShow(Alice, Severance, 1, 2);
            await Requests.AddSeasonsAsync(request.Id, UserFor(Alice), false, Actor.System, new SeasonChoice(false, System.Array.Empty<int>(), true));

            var picker = (await ClickOn(Alice, ulong.Parse(Reload(request.Id).MessageId), $"req-add:{request.Id}")).LastReply;

            picker.Content.Should().Contain(DiscordText.EverythingRequested);
            picker.SelectMenu.Should().BeNull();
            picker.Buttons.Select(b => b.Label).Should().Equal(DiscordText.BackLabel);
        }

        [Test]
        public async Task someone_who_neither_owns_nor_follows_it_cannot_add()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1);

            var run = await Harness.SendAsync(FakeInteraction.Button($"req-add-ok:{request.Id}:2").By(Bob).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Bob)));

            run.Responder.Replies.Last().Content.Should().Be(DiscordText.NotYourRequest);
            Reload(request.Id).Seasons.Should().Equal(1);
        }

        [Test]
        public async Task a_closed_request_takes_no_more_seasons()
        {
            await AskSeasons(Alice, DarkMatter2015, 1);
            await AdminModal("req-deny-reason:1", RequestRoutes.ReasonField, "no");

            var result = await Requests.AddSeasonsAsync(1, UserFor(Alice), false, Actor.System, new SeasonChoice(false, new[] { 2 }, false));

            result.Message.Should().Be(DiscordText.CannotAddSeasons(RequestStatus.Denied));
            Latest(Alice).Buttons.Should().BeEmpty("a denied request has no Add seasons");
        }

        [Test]
        public async Task adding_to_an_available_request_tracks_it_again()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1);
            Sonarr.Season(Series, 1, aired: 13, withFiles: 13);
            await Availability.CheckAllAsync();
            Reload(request.Id).Status.Should().Be(RequestStatus.Available);
            Latest(Alice).Buttons.Select(b => b.CustomId).Should().Contain($"req-add:{request.Id}", "a finished TV request can still grow");

            await Requests.AddSeasonsAsync(request.Id, UserFor(Alice), false, Actor.System, new SeasonChoice(false, new[] { 2 }, false));

            Reload(request.Id).Status.Should().Be(RequestStatus.Approved);
            Sonarr.Season(Series, 2, aired: 13, withFiles: 13);
            await Availability.CheckAllAsync();
            Reload(request.Id).Status.Should().Be(RequestStatus.Available);
            Reload(request.Id).AvailableSeasons.Should().Equal(1, 2);
        }

        [Test]
        public async Task back_returns_to_the_request_message()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1);
            var message = ulong.Parse(Reload(request.Id).MessageId);
            await ClickOn(Alice, message, $"req-add:{request.Id}");

            var back = (await ClickOn(Alice, message, $"req-add-back:{request.Id}")).LastReply;

            back.Content.Should().StartWith(DiscordText.FinalApproved);
            back.Buttons.Select(b => b.CustomId).Should().Equal($"req-add:{request.Id}");
        }

        [Test]
        public async Task sonarr_being_down_adds_nothing()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1);
            Sonarr.FailSeasonAdds = true;

            var result = await Requests.AddSeasonsAsync(request.Id, UserFor(Alice), false, Actor.System, new SeasonChoice(false, new[] { 2 }, false));

            result.Success.Should().BeFalse();
            Reload(request.Id).Seasons.Should().Equal(1);
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }
    }
}
