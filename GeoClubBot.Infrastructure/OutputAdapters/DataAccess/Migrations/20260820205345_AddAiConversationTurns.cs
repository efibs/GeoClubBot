using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddAiConversationTurns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiConversationTurns",
            columns: table => new
            {
                TurnId = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                DiscordMessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                ParentDiscordMessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                AuthorDiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                ImageUrls = table.Column<List<string>>(type: "text[]", nullable: false),
                ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Depth = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiConversationTurns", x => x.TurnId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiConversationTurns_AuthorDiscordUserId_CreatedAtUtc",
            table: "AiConversationTurns",
            columns: new[] { "AuthorDiscordUserId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AiConversationTurns_ConversationId",
            table: "AiConversationTurns",
            column: "ConversationId");

        migrationBuilder.CreateIndex(
            name: "IX_AiConversationTurns_CreatedAtUtc",
            table: "AiConversationTurns",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AiConversationTurns_DiscordMessageId",
            table: "AiConversationTurns",
            column: "DiscordMessageId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiConversationTurns");
    }
}
