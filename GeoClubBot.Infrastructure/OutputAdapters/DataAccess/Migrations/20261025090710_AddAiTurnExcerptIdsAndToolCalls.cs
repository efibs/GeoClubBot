using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddAiTurnExcerptIdsAndToolCalls : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The id lists are NOT NULL and both tables already hold rows, so existing turns are backfilled
        // with empty lists: answers recorded before this migration name no excerpts, which only means a
        // follow-up to one of them is offered nothing again. The tool calls are null for "none".
        migrationBuilder.AddColumn<List<Guid>>(
            name: "CitedChunkIds",
            table: "AiFeedbackTurns",
            type: "uuid[]",
            nullable: false,
            defaultValue: new List<Guid>());

        migrationBuilder.AddColumn<List<Guid>>(
            name: "OfferedChunkIds",
            table: "AiFeedbackTurns",
            type: "uuid[]",
            nullable: false,
            defaultValue: new List<Guid>());

        migrationBuilder.AddColumn<string>(
            name: "ToolCallsJson",
            table: "AiFeedbackTurns",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<List<Guid>>(
            name: "CitedChunkIds",
            table: "AiConversationTurns",
            type: "uuid[]",
            nullable: false,
            defaultValue: new List<Guid>());

        migrationBuilder.AddColumn<List<Guid>>(
            name: "OfferedChunkIds",
            table: "AiConversationTurns",
            type: "uuid[]",
            nullable: false,
            defaultValue: new List<Guid>());

        migrationBuilder.AddColumn<string>(
            name: "ToolCallsJson",
            table: "AiConversationTurns",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CitedChunkIds",
            table: "AiFeedbackTurns");

        migrationBuilder.DropColumn(
            name: "OfferedChunkIds",
            table: "AiFeedbackTurns");

        migrationBuilder.DropColumn(
            name: "ToolCallsJson",
            table: "AiFeedbackTurns");

        migrationBuilder.DropColumn(
            name: "CitedChunkIds",
            table: "AiConversationTurns");

        migrationBuilder.DropColumn(
            name: "OfferedChunkIds",
            table: "AiConversationTurns");

        migrationBuilder.DropColumn(
            name: "ToolCallsJson",
            table: "AiConversationTurns");
    }
}
