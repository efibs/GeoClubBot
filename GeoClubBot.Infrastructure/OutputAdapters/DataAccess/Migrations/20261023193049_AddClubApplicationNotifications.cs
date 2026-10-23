using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddClubApplicationNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClubApplicationNotifications",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false),
                NotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClubApplicationNotifications", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ClubApplicationNotifications_ClubId_UserId",
            table: "ClubApplicationNotifications",
            columns: new[] { "ClubId", "UserId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ClubApplicationNotifications");
    }
}
