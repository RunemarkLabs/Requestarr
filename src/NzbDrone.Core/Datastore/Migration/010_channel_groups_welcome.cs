using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the two channel groups (the admin group, and the request groups that become the users groups),
    // the welcome channel and its message, and the blocked role and channel. Also access requests from the welcome
    // channel, one row per person for the welcome buttons' spam guard, and a lasting record of every outage.
    [Migration(10)]
    public class channel_groups_welcome : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("DiscordGuildConfigs").AddColumn("AdminCategoryId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("WelcomeChannelId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("WelcomeMessageId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("BlockedRoleId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("BlockedChannelId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("BlockedMessageId").AsString().Nullable();

            Create.TableForModel("AccessRequests")
                .WithColumn("DiscordUserId").AsString().NotNullable().Indexed()
                .WithColumn("Username").AsString().Nullable()
                .WithColumn("DisplayName").AsString().Nullable()
                .WithColumn("ContentTypes").AsString().Nullable()
                .WithColumn("Note").AsString().Nullable()
                .WithColumn("MediaProvider").AsInt32().Nullable()
                .WithColumn("MediaUsername").AsString().Nullable()
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("Requested").AsDateTimeOffset().NotNullable()
                .WithColumn("Decided").AsDateTimeOffset().Nullable()
                .WithColumn("DecidedByUserId").AsInt32().Nullable()
                .WithColumn("DecidedByName").AsString().Nullable()
                .WithColumn("Reason").AsString().Nullable()
                .WithColumn("CardMessageId").AsString().Nullable();

            Create.TableForModel("WelcomeGuards")
                .WithColumn("DiscordUserId").AsString().NotNullable().Unique()
                .WithColumn("RecentClicks").AsString().Nullable()
                .WithColumn("CooldownUntil").AsDateTimeOffset().Nullable()
                .WithColumn("LongCooldownStarted").AsDateTimeOffset().Nullable()
                .WithColumn("ClicksSinceLong").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Blocked").AsDateTimeOffset().Nullable()
                .WithColumn("NoticeMessageId").AsString().Nullable();

            Create.TableForModel("Outages")
                .WithColumn("BackendId").AsInt32().NotNullable().Indexed()
                .WithColumn("InstanceName").AsString().Nullable()
                .WithColumn("Started").AsDateTimeOffset().NotNullable()
                .WithColumn("Ended").AsDateTimeOffset().Nullable()
                .WithColumn("Announced").AsDateTimeOffset().Nullable()
                .WithColumn("AnnouncedBy").AsString().Nullable();
        }
    }
}
