using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddPerformanceIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DailyMissionReminders_ReminderTimeUtc",
            table: "DailyMissionReminders");

        migrationBuilder.DropIndex(
            name: "IX_ClubMemberHistoryEntries_ClubId",
            table: "ClubMemberHistoryEntries");

        migrationBuilder.AlterColumn<Guid>(
            name: "ClubId",
            table: "ClubMembers",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.CreateIndex(
            name: "IX_DailyMissionReminders_ReminderTimeUtc_LastSentDateUtc",
            table: "DailyMissionReminders",
            columns: new[] { "ReminderTimeUtc", "LastSentDateUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ClubMemberHistoryEntries_ClubId_UserId_Timestamp",
            table: "ClubMemberHistoryEntries",
            columns: new[] { "ClubId", "UserId", "Timestamp" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DailyMissionReminders_ReminderTimeUtc_LastSentDateUtc",
            table: "DailyMissionReminders");

        migrationBuilder.DropIndex(
            name: "IX_ClubMemberHistoryEntries_ClubId_UserId_Timestamp",
            table: "ClubMemberHistoryEntries");

        migrationBuilder.AlterColumn<Guid>(
            name: "ClubId",
            table: "ClubMembers",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_DailyMissionReminders_ReminderTimeUtc",
            table: "DailyMissionReminders",
            column: "ReminderTimeUtc");

        migrationBuilder.CreateIndex(
            name: "IX_ClubMemberHistoryEntries_ClubId",
            table: "ClubMemberHistoryEntries",
            column: "ClubId");
    }
}
