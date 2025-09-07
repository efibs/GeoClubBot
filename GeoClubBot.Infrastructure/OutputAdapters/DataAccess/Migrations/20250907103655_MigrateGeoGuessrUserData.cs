using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class MigrateGeoGuessrUserData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                INSERT INTO ""GeoGuessrUsers"" (""UserId"", ""Nickname"")
                SELECT ""UserId"", ""Nickname"" 
                FROM ""ClubMembers"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE ""ClubMembers"" m
                    SET ""Nickname""=u.""Nickname""
                FROM ""GeoGuessrUsers"" u
                WHERE m.""UserId""=u.""UserId"";
            ");
        }
    }
}
