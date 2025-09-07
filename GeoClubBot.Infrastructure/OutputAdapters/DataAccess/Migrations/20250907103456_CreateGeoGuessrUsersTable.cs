using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CreateGeoGuessrUsersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeoGuessrAccountLinkingRequests",
                columns: table => new
                {
                    DiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    GeoGuessrUserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeoGuessrAccountLinkingRequests", x => new { x.DiscordUserId, x.GeoGuessrUserId });
                });

            migrationBuilder.CreateTable(
                name: "GeoGuessrUsers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Nickname = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeoGuessrUsers", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeoGuessrUsers_Nickname",
                table: "GeoGuessrUsers",
                column: "Nickname");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeoGuessrAccountLinkingRequests");

            migrationBuilder.DropTable(
                name: "GeoGuessrUsers");
        }
    }
}
