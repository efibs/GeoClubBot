using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ChangeKeysAndIndeces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubMemberStrikes_Timestamp",
                table: "ClubMemberStrikes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ClubMemberHistoryEntries",
                table: "ClubMemberHistoryEntries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ClubMemberHistoryEntries",
                table: "ClubMemberHistoryEntries",
                columns: new[] { "Timestamp", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberStrikes_Timestamp",
                table: "ClubMemberStrikes",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubMemberStrikes_Timestamp",
                table: "ClubMemberStrikes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ClubMemberHistoryEntries",
                table: "ClubMemberHistoryEntries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ClubMemberHistoryEntries",
                table: "ClubMemberHistoryEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberStrikes_Timestamp",
                table: "ClubMemberStrikes",
                column: "Timestamp",
                unique: true);
        }
    }
}
