using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddCountryChallenges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CountryChallengeLeaderboardPosts",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Season = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CountryChallengeLeaderboardPosts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "CountryChallengePosts",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ChallengeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                MapId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                MapName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                TimeLimit = table.Column<int>(type: "integer", nullable: false),
                ForbidMoving = table.Column<bool>(type: "boolean", nullable: false),
                ForbidRotating = table.Column<bool>(type: "boolean", nullable: false),
                ForbidZooming = table.Column<bool>(type: "boolean", nullable: false),
                ChallengeId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ResultsDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CountryChallengePosts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "CountryChallengePointAwards",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Season = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                Nickname = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Points = table.Column<int>(type: "integer", nullable: false),
                Place = table.Column<int>(type: "integer", nullable: true),
                PostId = table.Column<int>(type: "integer", nullable: true),
                Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CountryChallengePointAwards", x => x.Id);
                table.ForeignKey(
                    name: "FK_CountryChallengePointAwards_CountryChallengePosts_PostId",
                    column: x => x.PostId,
                    principalTable: "CountryChallengePosts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CountryChallengeLeaderboardPosts_Date",
            table: "CountryChallengeLeaderboardPosts",
            column: "Date",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CountryChallengePointAwards_PostId",
            table: "CountryChallengePointAwards",
            column: "PostId");

        migrationBuilder.CreateIndex(
            name: "IX_CountryChallengePointAwards_Season",
            table: "CountryChallengePointAwards",
            column: "Season");

        migrationBuilder.CreateIndex(
            name: "IX_CountryChallengePosts_ChallengeName_Date",
            table: "CountryChallengePosts",
            columns: new[] { "ChallengeName", "Date" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CountryChallengeLeaderboardPosts");

        migrationBuilder.DropTable(
            name: "CountryChallengePointAwards");

        migrationBuilder.DropTable(
            name: "CountryChallengePosts");
    }
}
