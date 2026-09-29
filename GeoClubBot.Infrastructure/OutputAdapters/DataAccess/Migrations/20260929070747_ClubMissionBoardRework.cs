using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class ClubMissionBoardRework : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // GeoGuessr replaced the daily mission with the club mission board on 2026-09-23; the
        // logged missions have no further use.
        migrationBuilder.DropTable(
            name: "DailyMissions");

        // The per-day completions are renamed rather than recreated: their daily-challenge
        // counts are the streak history, which the activity feed does not reach back to.
        migrationBuilder.RenameTable(
            name: "DailyMissionMemberCompletions",
            newName: "ClubMemberDailyActivities");

        migrationBuilder.Sql(
            "ALTER TABLE \"ClubMemberDailyActivities\" RENAME CONSTRAINT \"PK_DailyMissionMemberCompletions\" TO \"PK_ClubMemberDailyActivities\";");

        migrationBuilder.RenameIndex(
            name: "IX_DailyMissionMemberCompletions_ClubId_Date_UserId",
            table: "ClubMemberDailyActivities",
            newName: "IX_ClubMemberDailyActivities_ClubId_Date_UserId");

        migrationBuilder.RenameColumn(
            name: "CompletedCount",
            table: "ClubMemberDailyActivities",
            newName: "LegacyDailyMissionCount");

        migrationBuilder.AddColumn<int>(
            name: "BoardMissionCount",
            table: "ClubMemberDailyActivities",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "BoardClearBonusCount",
            table: "ClubMemberDailyActivities",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "Xp",
            table: "ClubMemberDailyActivities",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BoardMissionCount",
            table: "ClubMemberHistoryEntries",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "RuleXp",
            table: "ClubMemberHistoryEntries",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "StreakDays",
            table: "ClubMemberHistoryEntries",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MissionBoardAlerts",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                MissionId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MissionBoardAlerts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MissionBoardAlerts_ClubId_MissionId_Kind",
            table: "MissionBoardAlerts",
            columns: new[] { "ClubId", "MissionId", "Kind" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MissionBoardAlerts_SentAt",
            table: "MissionBoardAlerts",
            column: "SentAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MissionBoardAlerts");

        migrationBuilder.DropColumn(
            name: "BoardMissionCount",
            table: "ClubMemberDailyActivities");

        migrationBuilder.DropColumn(
            name: "BoardClearBonusCount",
            table: "ClubMemberDailyActivities");

        migrationBuilder.DropColumn(
            name: "Xp",
            table: "ClubMemberDailyActivities");

        migrationBuilder.RenameColumn(
            name: "LegacyDailyMissionCount",
            table: "ClubMemberDailyActivities",
            newName: "CompletedCount");

        migrationBuilder.RenameIndex(
            name: "IX_ClubMemberDailyActivities_ClubId_Date_UserId",
            table: "ClubMemberDailyActivities",
            newName: "IX_DailyMissionMemberCompletions_ClubId_Date_UserId");

        migrationBuilder.Sql(
            "ALTER TABLE \"ClubMemberDailyActivities\" RENAME CONSTRAINT \"PK_ClubMemberDailyActivities\" TO \"PK_DailyMissionMemberCompletions\";");

        migrationBuilder.RenameTable(
            name: "ClubMemberDailyActivities",
            newName: "DailyMissionMemberCompletions");

        migrationBuilder.DropColumn(
            name: "BoardMissionCount",
            table: "ClubMemberHistoryEntries");

        migrationBuilder.DropColumn(
            name: "RuleXp",
            table: "ClubMemberHistoryEntries");

        migrationBuilder.DropColumn(
            name: "StreakDays",
            table: "ClubMemberHistoryEntries");

        migrationBuilder.CreateTable(
            name: "DailyMissions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Completed = table.Column<bool>(type: "boolean", nullable: false),
                CurrentProgress = table.Column<int>(type: "integer", nullable: false),
                EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                GameMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                MapName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                MapSlug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                MissionId = table.Column<Guid>(type: "uuid", nullable: false),
                RewardAmount = table.Column<int>(type: "integer", nullable: false),
                RewardType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TargetProgress = table.Column<int>(type: "integer", nullable: false),
                Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DailyMissions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DailyMissions_FetchedAtUtc",
            table: "DailyMissions",
            column: "FetchedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_DailyMissions_MissionId",
            table: "DailyMissions",
            column: "MissionId");
    }
}
