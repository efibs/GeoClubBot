using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkingOneTimePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OneTimePassword",
                table: "GeoGuessrAccountLinkingRequests",
                type: "character varying(18)",
                maxLength: 18,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OneTimePassword",
                table: "GeoGuessrAccountLinkingRequests");
        }
    }
}
