using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddMemberPrivateChannelCategories : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MemberPrivateChannelCategories",
            columns: table => new
            {
                CategoryId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                Index = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MemberPrivateChannelCategories", x => x.CategoryId);
                table.ForeignKey(
                    name: "FK_MemberPrivateChannelCategories_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MemberPrivateChannelCategories_ClubId_Index",
            table: "MemberPrivateChannelCategories",
            columns: new[] { "ClubId", "Index" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MemberPrivateChannelCategories");
    }
}
