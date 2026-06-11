using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddDailyMissionMemberCompletions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DailyMissionMemberCompletions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                CompletedCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DailyMissionMemberCompletions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DailyMissionMemberCompletions_ClubId_Date_UserId",
            table: "DailyMissionMemberCompletions",
            columns: new[] { "ClubId", "Date", "UserId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DailyMissionMemberCompletions");
    }
}
