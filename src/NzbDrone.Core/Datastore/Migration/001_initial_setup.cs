using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr's initial schema. Servarr infrastructure tables keep their upstream
    // shape, with Prowlarr migrations 001-044 collapsed and Prowlarr domain tables dropped.
    // Requestarr's core data model is added alongside them.
    [Migration(1)]
    public class InitialSetup : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Servarr infrastructure
            Create.TableForModel("Config")
                  .WithColumn("Key").AsString().Unique()
                  .WithColumn("Value").AsString();

            Create.TableForModel("ScheduledTasks")
                .WithColumn("TypeName").AsString().Unique()
                .WithColumn("Interval").AsInt32()
                .WithColumn("LastExecution").AsDateTimeOffset()
                .WithColumn("LastStartTime").AsDateTimeOffset().Nullable();

            Create.TableForModel("Tags")
                .WithColumn("Label").AsString().Unique();

            // Web portal login (Forms / Basic auth). Not the Requestarr user model below.
            Create.TableForModel("Users")
                .WithColumn("Identifier").AsString().NotNullable().Unique()
                .WithColumn("Username").AsString().NotNullable().Unique()
                .WithColumn("Password").AsString().NotNullable()
                .WithColumn("Salt").AsString().Nullable()
                .WithColumn("Iterations").AsInt32().Nullable();

            Create.TableForModel("Commands")
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("Body").AsString().NotNullable()
                .WithColumn("Priority").AsInt32().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("QueuedAt").AsDateTimeOffset().NotNullable()
                .WithColumn("StartedAt").AsDateTimeOffset().Nullable()
                .WithColumn("EndedAt").AsDateTimeOffset().Nullable()
                .WithColumn("Duration").AsString().Nullable()
                .WithColumn("Exception").AsString().Nullable()
                .WithColumn("Trigger").AsInt32().NotNullable();

            Create.TableForModel("CustomFilters")
                .WithColumn("Type").AsString().NotNullable()
                .WithColumn("Label").AsString().NotNullable()
                .WithColumn("Filters").AsString().NotNullable();

            // Backend instances (Sonarr, Radarr, Lidarr) in the Servarr provider shape.
            // One row per configured instance, so any number of instances per type.
            Create.TableForModel("Backends")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("Implementation").AsString()
                .WithColumn("Settings").AsString().Nullable()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("Tags").AsString().Nullable()
                .WithColumn("Added").AsDateTimeOffset().NotNullable();

            // Requestarr core data model
            Create.TableForModel("RequestarrUsers")
                .WithColumn("DisplayName").AsString().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("DiscordRoleId").AsString().Nullable()
                .WithColumn("PrivateChannelId").AsString().Nullable()
                .WithColumn("Added").AsDateTimeOffset().NotNullable()
                .WithColumn("StatusChanged").AsDateTimeOffset().Nullable();

            Create.TableForModel("UserIdentities")
                .WithColumn("UserId").AsInt32().NotNullable().Indexed()
                .WithColumn("Provider").AsString().NotNullable()
                .WithColumn("ExternalId").AsString().NotNullable()
                .WithColumn("Username").AsString().Nullable()
                .WithColumn("DisplayName").AsString().Nullable()
                .WithColumn("AvatarUrl").AsString().Nullable()
                .WithColumn("Linked").AsDateTimeOffset().NotNullable();

            Create.Index("IX_UserIdentities_Provider_ExternalId").OnTable("UserIdentities")
                .OnColumn("Provider").Ascending()
                .OnColumn("ExternalId").Ascending()
                .WithOptions().Unique();

            Create.TableForModel("UserMediaAccess")
                .WithColumn("UserId").AsInt32().NotNullable()
                .WithColumn("ContentType").AsInt32().NotNullable()
                .WithColumn("State").AsInt32().NotNullable()
                .WithColumn("BackendId").AsInt32().Nullable()
                .WithColumn("QuotaLimit").AsInt32().Nullable()
                .WithColumn("QuotaWindow").AsString().Nullable();

            Create.Index("IX_UserMediaAccess_UserId_ContentType").OnTable("UserMediaAccess")
                .OnColumn("UserId").Ascending()
                .OnColumn("ContentType").Ascending()
                .WithOptions().Unique();

            Create.TableForModel("MediaServerAccounts")
                .WithColumn("UserId").AsInt32().NotNullable()
                .WithColumn("Provider").AsInt32().NotNullable()
                .WithColumn("Username").AsString().NotNullable()
                .WithColumn("ShareStatus").AsInt32().NotNullable()
                .WithColumn("Added").AsDateTimeOffset().NotNullable();

            Create.Index("IX_MediaServerAccounts_UserId_Provider").OnTable("MediaServerAccounts")
                .OnColumn("UserId").Ascending()
                .OnColumn("Provider").Ascending()
                .WithOptions().Unique();

            Create.TableForModel("MediaServerLibraryMappings")
                .WithColumn("Provider").AsInt32().NotNullable()
                .WithColumn("LibraryId").AsString().NotNullable()
                .WithColumn("LibraryName").AsString().NotNullable()
                .WithColumn("ContentType").AsInt32().NotNullable();

            Create.Index("IX_MediaServerLibraryMappings_Provider_LibraryId").OnTable("MediaServerLibraryMappings")
                .OnColumn("Provider").Ascending()
                .OnColumn("LibraryId").Ascending()
                .WithOptions().Unique();

            Create.TableForModel("MediaServerAccessRequests")
                .WithColumn("UserId").AsInt32().NotNullable().Indexed()
                .WithColumn("Provider").AsInt32().NotNullable()
                .WithColumn("Username").AsString().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("DecidedByUserId").AsInt32().Nullable()
                .WithColumn("Requested").AsDateTimeOffset().NotNullable()
                .WithColumn("Decided").AsDateTimeOffset().Nullable();

            Create.TableForModel("Requests")
                .WithColumn("OwnerUserId").AsInt32().NotNullable().Indexed()
                .WithColumn("ContentType").AsInt32().NotNullable()
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("Year").AsInt32().Nullable()
                .WithColumn("TmdbId").AsInt32().Nullable()
                .WithColumn("TvdbId").AsInt32().Nullable()
                .WithColumn("MusicBrainzId").AsString().Nullable()
                .WithColumn("ComicVineId").AsString().Nullable()
                .WithColumn("Isbn").AsString().Nullable()
                .WithColumn("Status").AsInt32().NotNullable().Indexed()
                .WithColumn("BackendId").AsInt32().Nullable()
                .WithColumn("QualityProfileId").AsInt32().Nullable()
                .WithColumn("RootFolderPath").AsString().Nullable()
                .WithColumn("Seasons").AsString().Nullable()
                .WithColumn("MonitorFutureSeasons").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("DecidedByUserId").AsInt32().Nullable()
                .WithColumn("DecisionReason").AsString().Nullable()
                .WithColumn("ExternalItemId").AsInt32().Nullable()
                .WithColumn("Requested").AsDateTimeOffset().NotNullable()
                .WithColumn("Decided").AsDateTimeOffset().Nullable()
                .WithColumn("Fulfilled").AsDateTimeOffset().Nullable();

            Create.Index("IX_Requests_ContentType_TmdbId").OnTable("Requests")
                .OnColumn("ContentType").Ascending()
                .OnColumn("TmdbId").Ascending();

            Create.Index("IX_Requests_ContentType_TvdbId").OnTable("Requests")
                .OnColumn("ContentType").Ascending()
                .OnColumn("TvdbId").Ascending();

            Create.TableForModel("RequestSubscribers")
                .WithColumn("RequestId").AsInt32().NotNullable()
                .WithColumn("UserId").AsInt32().NotNullable().Indexed()
                .WithColumn("Added").AsDateTimeOffset().NotNullable();

            Create.Index("IX_RequestSubscribers_RequestId_UserId").OnTable("RequestSubscribers")
                .OnColumn("RequestId").Ascending()
                .OnColumn("UserId").Ascending()
                .WithOptions().Unique();

            Create.TableForModel("RemovalProposals")
                .WithColumn("RequestId").AsInt32().Nullable().Indexed()
                .WithColumn("ContentType").AsInt32().NotNullable()
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("BackendId").AsInt32().Nullable()
                .WithColumn("ExternalItemId").AsInt32().Nullable()
                .WithColumn("Seasons").AsString().Nullable()
                .WithColumn("InitiatedByUserId").AsInt32().Nullable()
                .WithColumn("InitiatedByAdmin").AsBoolean().NotNullable()
                .WithColumn("RequesterResponse").AsInt32().Nullable()
                .WithColumn("Status").AsInt32().NotNullable().Indexed()
                .WithColumn("Created").AsDateTimeOffset().NotNullable()
                .WithColumn("WindowClosesAt").AsDateTimeOffset().Nullable()
                .WithColumn("Closed").AsDateTimeOffset().Nullable();

            Create.TableForModel("RemovalFlags")
                .WithColumn("ProposalId").AsInt32().NotNullable()
                .WithColumn("UserId").AsInt32().NotNullable()
                .WithColumn("FlaggedAt").AsDateTimeOffset().NotNullable();

            Create.Index("IX_RemovalFlags_ProposalId_UserId").OnTable("RemovalFlags")
                .OnColumn("ProposalId").Ascending()
                .OnColumn("UserId").Ascending()
                .WithOptions().Unique();

            Create.TableForModel("AuditEntries")
                .WithColumn("ActorUserId").AsInt32().Nullable()
                .WithColumn("Actor").AsString().NotNullable()
                .WithColumn("Action").AsString().NotNullable()
                .WithColumn("TargetType").AsString().Nullable()
                .WithColumn("TargetId").AsString().Nullable()
                .WithColumn("Timestamp").AsDateTimeOffset().NotNullable().Indexed()
                .WithColumn("Detail").AsString().Nullable();

            Create.TableForModel("DiscordGuildConfigs")
                .WithColumn("GuildId").AsString().NotNullable().Unique()
                .WithColumn("AdminRoleId").AsString().Nullable()
                .WithColumn("AdminQueueChannelId").AsString().Nullable()
                .WithColumn("RequestCategoryIds").AsString().Nullable();
        }

        protected override void LogDbUpgrade()
        {
            Create.TableForModel("Logs")
                  .WithColumn("Message").AsString()
                  .WithColumn("Time").AsDateTimeOffset().Indexed()
                  .WithColumn("Logger").AsString()
                  .WithColumn("Exception").AsString().Nullable()
                  .WithColumn("ExceptionType").AsString().Nullable()
                  .WithColumn("Level").AsString();

            Create.TableForModel("UpdateHistory")
                  .WithColumn("Date").AsDateTimeOffset().NotNullable().Indexed()
                  .WithColumn("Version").AsString().NotNullable()
                  .WithColumn("EventType").AsInt32().NotNullable();
        }
    }
}
