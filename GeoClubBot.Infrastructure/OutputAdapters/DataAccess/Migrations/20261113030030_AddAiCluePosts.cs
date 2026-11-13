using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddAiCluePosts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiCluePosts",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                MessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                PostedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiCluePosts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiCluePosts_ChunkId",
            table: "AiCluePosts",
            column: "ChunkId");

        migrationBuilder.CreateIndex(
            name: "IX_AiCluePosts_PostedAtUtc",
            table: "AiCluePosts",
            column: "PostedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiCluePosts");
    }
}
