using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Test.AnimeDub;

namespace Requestarr.Core.Test.Discord
{
    // The bot keeps its own Discord picture and banner up to date, without overwriting one someone chose.
    [TestFixture]
    public class BotProfileFixture : CoreTest<BotProfileService>
    {
        private static readonly BotImageRecord Ours = new("ourid", "ourfile");

        [TestCase(false)]
        [TestCase(true)]
        public void a_bot_without_a_picture_gets_requestarrs(bool takeOver)
        {
            BotProfileService.Decide(null, null, "ourfile", takeOver).Should().Be(BotImageAction.Set);
            BotProfileService.Decide(null, Ours, "ourfile", takeOver).Should().Be(BotImageAction.Set);
        }

        [Test]
        public void requestarrs_own_picture_is_left_when_current_and_replaced_when_a_release_brings_a_new_one()
        {
            BotProfileService.Decide("ourid", Ours, "ourfile", false).Should().Be(BotImageAction.UpToDate);
            BotProfileService.Decide("ourid", Ours, "newfile", false).Should().Be(BotImageAction.Set);
        }

        [Test]
        public void a_picture_someone_chose_is_left_alone_unless_take_over_is_on()
        {
            // Changed by hand after Requestarr set it: Discord gave it a new id.
            BotProfileService.Decide("theirid", Ours, "ourfile", false).Should().Be(BotImageAction.LeftAlone);
            BotProfileService.Decide("theirid", Ours, "ourfile", true).Should().Be(BotImageAction.Set);

            // There before Requestarr managed it.
            BotProfileService.Decide("theirid", null, "ourfile", false).Should().Be(BotImageAction.LeftAlone);
            BotProfileService.Decide("theirid", null, "ourfile", true).Should().Be(BotImageAction.Set);
        }

        [TestCase("dev", "requestarr-banner-dev.png")]
        [TestCase("qa", "requestarr-banner-qa.png")]
        [TestCase("beta", "requestarr-banner-beta.png")]
        [TestCase("production", "requestarr-banner-teal.png")]
        public void each_environment_has_its_banner_and_every_bot_the_same_picture(string iconSet, string banner)
        {
            BotProfileService.BannerFile(iconSet).Should().Be(banner);
            DiscordAssets.Bytes(banner).Should().NotBeNullOrEmpty();
            DiscordAssets.Bytes(BotProfileService.AvatarFile).Should().NotBeNullOrEmpty();
        }

        [Test]
        public void the_built_in_picture_and_banners_are_the_delivered_files()
        {
            foreach (var file in new[] { "requestarr-avatar.png", "requestarr-banner-dev.png", "requestarr-banner-qa.png", "requestarr-banner-beta.png", "requestarr-banner-teal.png" })
            {
                DiscordAssets.Bytes(file).Should().Equal(File.ReadAllBytes(Item4To6Fixture.RepoFile("src", "NzbDrone.Core", "Requestarr", "Discord", "Assets", file)), file);
            }
        }

        [Test]
        public void what_requestarr_set_is_remembered_as_discords_id_and_the_files_digest()
        {
            var record = new BotImageRecord("a1b2", BotProfileService.Digest(new byte[] { 1, 2, 3 }));

            BotImageRecord.Parse(record.ToString()).Should().Be(record);
            BotImageRecord.Parse(string.Empty).Should().BeNull();
            BotImageRecord.Parse("|x").Should().BeNull();
            BotProfileService.Digest(new byte[] { 1, 2, 3 }).Should().Be("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81");
        }

        [Test]
        public void provisioning_sets_the_settings_it_names_and_leaves_the_rest()
        {
            var config = new Mock<IConfigService>();
            var settings = new BotProfileSettings(config.Object);

            settings.Apply(new BotProfileProvisioning { KeepUpToDate = false });
            config.VerifySet(c => c.BotProfileKeepUpToDate = false);
            config.VerifySet(c => c.BotProfileTakeOver = It.IsAny<bool>(), Times.Never());

            settings.Apply(null);
            config.VerifySet(c => c.BotProfileKeepUpToDate = It.IsAny<bool>(), Times.Once());
        }

        [Test]
        public void the_settings_are_read_from_the_provisioning_file()
        {
            var document = new ProvisioningLoader(null, null).Parse("botProfile:\n  keepUpToDate: false\n  takeOver: true\n");

            document.BotProfile.KeepUpToDate.Should().BeFalse();
            document.BotProfile.TakeOver.Should().BeTrue();
        }

        [Test]
        public void when_it_is_off_the_bot_is_not_touched()
        {
            Mocker.GetMock<IBotProfileSettings>().SetupGet(s => s.KeepUpToDate).Returns(false);

            Subject.SyncAsync().GetAwaiter().GetResult();

            Mocker.GetMock<IDiscordClientHost>().VerifyGet(h => h.Client, Times.Never());
            Mocker.GetMock<IConfigService>().VerifySet(c => c.BotProfileAvatarSet = It.IsAny<string>(), Times.Never());
        }

        [Test]
        public void with_no_bot_connected_nothing_happens_and_nothing_fails()
        {
            Mocker.GetMock<IBotProfileSettings>().SetupGet(s => s.KeepUpToDate).Returns(true);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.IconSet).Returns("dev");

            Subject.Invoking(s => s.SyncAsync().GetAwaiter().GetResult()).Should().NotThrow();
            Mocker.GetMock<IConfigService>().VerifySet(c => c.BotProfileAvatarSet = It.IsAny<string>(), Times.Never());
        }
    }
}
