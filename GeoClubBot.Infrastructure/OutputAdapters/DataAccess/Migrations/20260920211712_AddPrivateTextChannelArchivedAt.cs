using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddPrivateTextChannelArchivedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No backfill needed: until now the channel of anyone who left a club was deleted
        // outright, so every existing row's channel (if any) is live, which null already means.
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PrivateTextChannelArchivedAt",
            table: "ClubMembers",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PrivateTextChannelArchivedAt",
            table: "ClubMembers");
    }
}
