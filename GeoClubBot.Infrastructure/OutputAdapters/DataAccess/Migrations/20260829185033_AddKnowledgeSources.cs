using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddKnowledgeSources : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "KnowledgeSources",
            columns: table => new
            {
                SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                NaturalKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Country = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Continent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Author = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Priority = table.Column<int>(type: "integer", nullable: false),
                Origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                StatusReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                SourceUpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastAttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastIngestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                ChunkCount = table.Column<int>(type: "integer", nullable: false),
                ImageCount = table.Column<int>(type: "integer", nullable: false),
                FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RemovedFromSyncAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KnowledgeSources", x => x.SourceId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeSources_SourceType_NaturalKey",
            table: "KnowledgeSources",
            columns: new[] { "SourceType", "NaturalKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeSources_Status_LastAttemptedAtUtc",
            table: "KnowledgeSources",
            columns: new[] { "Status", "LastAttemptedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "KnowledgeSources");
    }
}
