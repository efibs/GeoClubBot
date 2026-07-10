using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.OutputAdapters.DataAccess.Migrations;

/// <inheritdoc />
public partial class BackfillClubLatestActivityCheckTime : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The activity check now tracks its last-run time on Club.LatestActivityCheckTime, which
        // historically was never persisted (always null). Backfill it from the most recent history
        // entry per club — the value the check used to derive on the fly — so the first check after
        // this deployment keeps the same activity-check interval instead of restarting from scratch.
        migrationBuilder.Sql(
            """
            UPDATE "Clubs" AS c
            SET "LatestActivityCheckTime" = h.max_timestamp
            FROM (
                SELECT "ClubId", MAX("Timestamp") AS max_timestamp
                FROM "ClubMemberHistoryEntries"
                GROUP BY "ClubId"
            ) AS h
            WHERE c."ClubId" = h."ClubId"
              AND c."LatestActivityCheckTime" IS NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data backfill only; there is no meaningful, non-destructive way to distinguish a
        // backfilled value from one written by a later activity check, so this is intentionally
        // not reversible.
    }
}
