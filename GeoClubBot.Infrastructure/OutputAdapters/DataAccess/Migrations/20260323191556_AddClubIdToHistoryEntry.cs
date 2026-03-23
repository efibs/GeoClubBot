using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddClubIdToHistoryEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClubId",
                table: "ClubMemberHistoryEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE "ClubMemberHistoryEntries" h
                SET "ClubId" = m."ClubId"
                FROM "ClubMembers" m
                WHERE h."UserId" = m."UserId"
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberHistoryEntries_ClubId",
                table: "ClubMemberHistoryEntries",
                column: "ClubId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClubMemberHistoryEntries_Clubs_ClubId",
                table: "ClubMemberHistoryEntries",
                column: "ClubId",
                principalTable: "Clubs",
                principalColumn: "ClubId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClubMemberHistoryEntries_Clubs_ClubId",
                table: "ClubMemberHistoryEntries");

            migrationBuilder.DropIndex(
                name: "IX_ClubMemberHistoryEntries_ClubId",
                table: "ClubMemberHistoryEntries");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "ClubMemberHistoryEntries");
        }
    }
}
