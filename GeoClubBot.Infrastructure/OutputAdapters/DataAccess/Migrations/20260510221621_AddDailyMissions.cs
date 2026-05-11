using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyMissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyMissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GameMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentProgress = table.Column<int>(type: "integer", nullable: false),
                    TargetProgress = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RewardAmount = table.Column<int>(type: "integer", nullable: false),
                    RewardType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyMissions");
        }
    }
}
