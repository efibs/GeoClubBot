using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddMissionClaims : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MissionClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                MissionId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false),
                ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MissionClaims", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MissionClaims_ClubId_ClaimedAt",
            table: "MissionClaims",
            columns: new[] { "ClubId", "ClaimedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_MissionClaims_ClubId_PeriodStart_MissionId",
            table: "MissionClaims",
            columns: new[] { "ClubId", "PeriodStart", "MissionId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MissionClaims_UserId_ClaimedAt",
            table: "MissionClaims",
            columns: new[] { "UserId", "ClaimedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MissionClaims");
    }
}
