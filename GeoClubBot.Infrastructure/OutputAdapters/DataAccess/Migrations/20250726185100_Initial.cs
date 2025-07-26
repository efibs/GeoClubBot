using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    LatestActivityCheckTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.ClubId);
                });

            migrationBuilder.CreateTable(
                name: "ClubMembers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubMembers", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_ClubMembers_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "ClubId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClubMemberExcuses",
                columns: table => new
                {
                    ExcuseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    From = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubMemberExcuses", x => x.ExcuseId);
                    table.ForeignKey(
                        name: "FK_ClubMemberExcuses_ClubMembers_UserId",
                        column: x => x.UserId,
                        principalTable: "ClubMembers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClubMemberHistoryEntries",
                columns: table => new
                {
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubMemberHistoryEntries", x => x.Timestamp);
                    table.ForeignKey(
                        name: "FK_ClubMemberHistoryEntries_ClubMembers_UserId",
                        column: x => x.UserId,
                        principalTable: "ClubMembers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClubMemberStrikes",
                columns: table => new
                {
                    StrikeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubMemberStrikes", x => x.StrikeId);
                    table.ForeignKey(
                        name: "FK_ClubMemberStrikes_ClubMembers_UserId",
                        column: x => x.UserId,
                        principalTable: "ClubMembers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberExcuses_To",
                table: "ClubMemberExcuses",
                column: "To");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberExcuses_UserId",
                table: "ClubMemberExcuses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberHistoryEntries_UserId",
                table: "ClubMemberHistoryEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMembers_ClubId",
                table: "ClubMembers",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMembers_Nickname",
                table: "ClubMembers",
                column: "Nickname");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberStrikes_Timestamp",
                table: "ClubMemberStrikes",
                column: "Timestamp",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberStrikes_UserId",
                table: "ClubMemberStrikes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubMemberExcuses");

            migrationBuilder.DropTable(
                name: "ClubMemberHistoryEntries");

            migrationBuilder.DropTable(
                name: "ClubMemberStrikes");

            migrationBuilder.DropTable(
                name: "ClubMembers");

            migrationBuilder.DropTable(
                name: "Clubs");
        }
    }
}
