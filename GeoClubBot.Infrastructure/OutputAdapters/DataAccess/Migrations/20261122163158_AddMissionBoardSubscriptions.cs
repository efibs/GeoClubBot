using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddMissionBoardSubscriptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClubMissionBoardObservations",
            columns: table => new
            {
                ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CurrentBoardNumber = table.Column<int>(type: "integer", nullable: false),
                AllBoardsCleared = table.Column<bool>(type: "boolean", nullable: false),
                ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClubMissionBoardObservations", x => x.ClubId);
            });

        migrationBuilder.CreateTable(
            name: "MissionBoardSubscriptions",
            columns: table => new
            {
                DiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                Mode = table.Column<int>(type: "integer", nullable: false),
                SubscribedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MissionBoardSubscriptions", x => x.DiscordUserId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ClubMissionBoardObservations");

        migrationBuilder.DropTable(
            name: "MissionBoardSubscriptions");
    }
}
