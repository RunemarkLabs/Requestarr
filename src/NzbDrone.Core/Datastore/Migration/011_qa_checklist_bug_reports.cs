using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the QA test checklist (its channel and summary card, and who passed or failed each item and when), and
    // bug reports from the Report a problem button (its channel and message, each report and the issue it became, and
    // one row per person for the report limit).
    [Migration(11)]
    public class qa_checklist_bug_reports : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("DiscordGuildConfigs").AddColumn("QaChecklistChannelId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("QaChecklistSummaryMessageId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("BugReportsChannelId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("BugReportsMessageId").AsString().Nullable();

            Create.TableForModel("QaChecklistItems")
                .WithColumn("ItemKey").AsString().NotNullable().Unique()
                .WithColumn("Version").AsString().NotNullable()
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ByDiscordUserId").AsString().Nullable()
                .WithColumn("ByName").AsString().Nullable()
                .WithColumn("At").AsDateTimeOffset().Nullable()
                .WithColumn("BugReportId").AsInt32().Nullable()
                .WithColumn("MessageId").AsString().Nullable();

            Create.TableForModel("BugReports")
                .WithColumn("Component").AsString().NotNullable()
                .WithColumn("WhatHappened").AsString().NotNullable()
                .WithColumn("Expected").AsString().Nullable()
                .WithColumn("Environment").AsString().NotNullable()
                .WithColumn("Version").AsString().NotNullable()
                .WithColumn("ReporterDiscordId").AsString().NotNullable().Indexed()
                .WithColumn("ReporterName").AsString().Nullable()
                .WithColumn("Created").AsDateTimeOffset().NotNullable()
                .WithColumn("ChecklistItemKey").AsString().Nullable()
                .WithColumn("Filing").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("IssueNumber").AsInt32().Nullable()
                .WithColumn("IssueUrl").AsString().Nullable()
                .WithColumn("ChannelId").AsString().Nullable()
                .WithColumn("MessageId").AsString().Nullable();

            Create.TableForModel("BugReportGuards")
                .WithColumn("DiscordUserId").AsString().NotNullable().Unique()
                .WithColumn("RecentReports").AsString().Nullable();
        }
    }
}
