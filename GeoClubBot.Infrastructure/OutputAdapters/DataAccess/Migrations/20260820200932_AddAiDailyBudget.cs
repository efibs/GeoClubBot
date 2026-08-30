using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddAiDailyBudget : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiDailyBudgets",
            columns: table => new
            {
                DateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                RequestCount = table.Column<int>(type: "integer", nullable: false),
                PromptTokens = table.Column<long>(type: "bigint", nullable: false),
                CompletionTokens = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiDailyBudgets", x => x.DateUtc);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiDailyBudgets");
    }
}
