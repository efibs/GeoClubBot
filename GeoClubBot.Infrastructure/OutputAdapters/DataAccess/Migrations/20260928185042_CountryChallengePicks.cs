using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class CountryChallengePicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CountryChallengePosts_ChallengeName_Date",
            table: "CountryChallengePosts");

        migrationBuilder.CreateIndex(
            name: "IX_CountryChallengePosts_ChallengeName_Date_Country",
            table: "CountryChallengePosts",
            columns: new[] { "ChallengeName", "Date", "Country" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CountryChallengePosts_ChallengeName_Date_Country",
            table: "CountryChallengePosts");

        migrationBuilder.CreateIndex(
            name: "IX_CountryChallengePosts_ChallengeName_Date",
            table: "CountryChallengePosts",
            columns: new[] { "ChallengeName", "Date" },
            unique: true);
    }
}
