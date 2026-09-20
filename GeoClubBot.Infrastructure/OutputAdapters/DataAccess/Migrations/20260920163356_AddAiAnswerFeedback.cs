using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddAiAnswerFeedback : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiAnswerFeedbacks",
            columns: table => new
            {
                FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                RatedDiscordMessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                ConversationId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                ReviewerDiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                Rating = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                AnswerDepth = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiAnswerFeedbacks", x => x.FeedbackId);
            });

        migrationBuilder.CreateTable(
            name: "AiFeedbackTurns",
            columns: table => new
            {
                FeedbackTurnId = table.Column<Guid>(type: "uuid", nullable: false),
                FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                Ordinal = table.Column<int>(type: "integer", nullable: false),
                Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                AuthorDiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                DiscordMessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                ImageUrls = table.Column<List<string>>(type: "text[]", nullable: false),
                ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiFeedbackTurns", x => x.FeedbackTurnId);
                table.ForeignKey(
                    name: "FK_AiFeedbackTurns_AiAnswerFeedbacks_FeedbackId",
                    column: x => x.FeedbackId,
                    principalTable: "AiAnswerFeedbacks",
                    principalColumn: "FeedbackId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiAnswerFeedbacks_ConversationId",
            table: "AiAnswerFeedbacks",
            column: "ConversationId");

        migrationBuilder.CreateIndex(
            name: "IX_AiAnswerFeedbacks_CreatedAtUtc",
            table: "AiAnswerFeedbacks",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AiAnswerFeedbacks_RatedDiscordMessageId_ReviewerDiscordUser~",
            table: "AiAnswerFeedbacks",
            columns: new[] { "RatedDiscordMessageId", "ReviewerDiscordUserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AiAnswerFeedbacks_Rating",
            table: "AiAnswerFeedbacks",
            column: "Rating");

        migrationBuilder.CreateIndex(
            name: "IX_AiFeedbackTurns_FeedbackId_Ordinal",
            table: "AiFeedbackTurns",
            columns: new[] { "FeedbackId", "Ordinal" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiFeedbackTurns");

        migrationBuilder.DropTable(
            name: "AiAnswerFeedbacks");
    }
}
