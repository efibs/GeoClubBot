using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddDailyMissionReminderId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Re-key the table from DiscordUserId to a surrogate Id so a user can have several
        // reminders. Existing rows are preserved: each keeps its DiscordUserId, time, message
        // and last-sent date, and is simply assigned its own fresh Id below.
        migrationBuilder.DropPrimaryKey(
            name: "PK_DailyMissionReminders",
            table: "DailyMissionReminders");

        // Add the column nullable first so the backfill can give every existing row a unique value
        // before it becomes the (non-null) primary key.
        migrationBuilder.AddColumn<Guid>(
            name: "Id",
            table: "DailyMissionReminders",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"DailyMissionReminders\" SET \"Id\" = gen_random_uuid() WHERE \"Id\" IS NULL;");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "DailyMissionReminders",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AddPrimaryKey(
            name: "PK_DailyMissionReminders",
            table: "DailyMissionReminders",
            column: "Id");

        migrationBuilder.CreateIndex(
            name: "IX_DailyMissionReminders_DiscordUserId",
            table: "DailyMissionReminders",
            column: "DiscordUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverting to a DiscordUserId primary key only succeeds if no user has more than one
        // reminder; delete the extra reminders per user before rolling back if that has happened.
        migrationBuilder.DropPrimaryKey(
            name: "PK_DailyMissionReminders",
            table: "DailyMissionReminders");

        migrationBuilder.DropIndex(
            name: "IX_DailyMissionReminders_DiscordUserId",
            table: "DailyMissionReminders");

        migrationBuilder.DropColumn(
            name: "Id",
            table: "DailyMissionReminders");

        migrationBuilder.AddPrimaryKey(
            name: "PK_DailyMissionReminders",
            table: "DailyMissionReminders",
            column: "DiscordUserId");
    }
}
