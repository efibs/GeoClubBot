using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddAiRetrievalGaps : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiRetrievalGaps",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AskedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Country = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Topic = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Reason = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiRetrievalGaps", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiRetrievalGaps_AskedAtUtc",
            table: "AiRetrievalGaps",
            column: "AskedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiRetrievalGaps");
    }
}
