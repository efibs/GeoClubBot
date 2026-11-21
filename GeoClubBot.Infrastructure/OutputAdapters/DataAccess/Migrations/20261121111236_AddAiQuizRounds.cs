using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddAiQuizRounds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiQuizItems",
            columns: table => new
            {
                ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Country = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                SourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ModelId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                RatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiQuizItems", x => x.ChunkId);
            });

        migrationBuilder.CreateTable(
            name: "AiQuizRounds",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Options = table.Column<List<string>>(type: "text[]", nullable: false),
                CorrectIndex = table.Column<int>(type: "integer", nullable: false),
                CountryName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Guide = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Section = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                Explanation = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                AskedCountry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                LesserKnownOnly = table.Column<bool>(type: "boolean", nullable: false),
                AskedDifficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                MessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                AskedByUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                AskedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RevealAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RevealedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiQuizRounds", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AiQuizItemQuestions",
            columns: table => new
            {
                ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Answer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Decoys = table.Column<List<string>>(type: "text[]", nullable: false),
                Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiQuizItemQuestions", x => new { x.ChunkId, x.Kind });
                table.ForeignKey(
                    name: "FK_AiQuizItemQuestions_AiQuizItems_ChunkId",
                    column: x => x.ChunkId,
                    principalTable: "AiQuizItems",
                    principalColumn: "ChunkId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AiQuizGuesses",
            columns: table => new
            {
                RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                OptionIndex = table.Column<int>(type: "integer", nullable: false),
                Correct = table.Column<bool>(type: "boolean", nullable: false),
                Points = table.Column<int>(type: "integer", nullable: false),
                AnsweredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiQuizGuesses", x => new { x.RoundId, x.UserId });
                table.ForeignKey(
                    name: "FK_AiQuizGuesses_AiQuizRounds_RoundId",
                    column: x => x.RoundId,
                    principalTable: "AiQuizRounds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiQuizGuesses_AnsweredAtUtc_UserId",
            table: "AiQuizGuesses",
            columns: new[] { "AnsweredAtUtc", "UserId" });

        migrationBuilder.CreateIndex(
            name: "IX_AiQuizItemQuestions_Kind_Difficulty",
            table: "AiQuizItemQuestions",
            columns: new[] { "Kind", "Difficulty" });

        migrationBuilder.CreateIndex(
            name: "IX_AiQuizItems_Version_Country",
            table: "AiQuizItems",
            columns: new[] { "Version", "Country" });

        migrationBuilder.CreateIndex(
            name: "IX_AiQuizRounds_ChunkId",
            table: "AiQuizRounds",
            column: "ChunkId");

        migrationBuilder.CreateIndex(
            name: "IX_AiQuizRounds_RevealedAtUtc_RevealAtUtc",
            table: "AiQuizRounds",
            columns: new[] { "RevealedAtUtc", "RevealAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiQuizGuesses");

        migrationBuilder.DropTable(
            name: "AiQuizItemQuestions");

        migrationBuilder.DropTable(
            name: "AiQuizRounds");

        migrationBuilder.DropTable(
            name: "AiQuizItems");
    }
}
