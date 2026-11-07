using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddClubChatForwards : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClubChatForwards",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DiscordMessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                ForwardedByDiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                ForwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PartCount = table.Column<int>(type: "integer", nullable: false),
                PartsSent = table.Column<int>(type: "integer", nullable: false),
                PartsConfirmed = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClubChatForwards", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ClubChatForwards_DiscordMessageId_ClubId",
            table: "ClubChatForwards",
            columns: new[] { "DiscordMessageId", "ClubId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ClubChatForwards");
    }
}
