using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RemoveClubMemberNicknameColumnAndAddUserForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubMembers_Nickname",
                table: "ClubMembers");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "ClubMembers");

            migrationBuilder.AddForeignKey(
                name: "FK_ClubMembers_GeoGuessrUsers_UserId",
                table: "ClubMembers",
                column: "UserId",
                principalTable: "GeoGuessrUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClubMembers_GeoGuessrUsers_UserId",
                table: "ClubMembers");

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "ClubMembers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMembers_Nickname",
                table: "ClubMembers",
                column: "Nickname");
        }
    }
}
