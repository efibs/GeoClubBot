using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddAiTurnSourcesAndChunks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Every column here is NOT NULL, and AiConversationTurns already holds rows, so each one
        // needs something to backfill existing turns with. Answers recorded before this
        // migration simply have no retrieval trace.
        migrationBuilder.AddColumn<List<string>>(
            name: "CitedSourceUrls",
            table: "AiFeedbackTurns",
            type: "text[]",
            nullable: false,
            defaultValue: new List<string>());

        migrationBuilder.AddColumn<List<string>>(
            name: "RetrievedSourceUrls",
            table: "AiFeedbackTurns",
            type: "text[]",
            nullable: false,
            defaultValue: new List<string>());

        migrationBuilder.AddColumn<decimal[]>(
            name: "ChunkMessageIds",
            table: "AiConversationTurns",
            type: "numeric(20,0)[]",
            nullable: false,
            defaultValue: new decimal[0]);

        migrationBuilder.AddColumn<List<string>>(
            name: "CitedSourceUrls",
            table: "AiConversationTurns",
            type: "text[]",
            nullable: false,
            defaultValue: new List<string>());

        migrationBuilder.AddColumn<List<string>>(
            name: "RetrievedSourceUrls",
            table: "AiConversationTurns",
            type: "text[]",
            nullable: false,
            defaultValue: new List<string>());
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CitedSourceUrls",
            table: "AiFeedbackTurns");

        migrationBuilder.DropColumn(
            name: "RetrievedSourceUrls",
            table: "AiFeedbackTurns");

        migrationBuilder.DropColumn(
            name: "ChunkMessageIds",
            table: "AiConversationTurns");

        migrationBuilder.DropColumn(
            name: "CitedSourceUrls",
            table: "AiConversationTurns");

        migrationBuilder.DropColumn(
            name: "RetrievedSourceUrls",
            table: "AiConversationTurns");
    }
}
