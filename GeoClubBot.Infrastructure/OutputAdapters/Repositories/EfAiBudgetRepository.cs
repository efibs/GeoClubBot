using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfAiBudgetRepository(GeoClubBotDbContext dbContext) : IAiBudgetRepository
{
    public async Task<bool> TryReserveRequestsAsync(
        DateOnly dateUtc,
        int amount,
        int dailyCap,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return true;
        }

        // Guarded here rather than in SQL: the INSERT branch below creates the day's first row and
        // has no existing count to compare against, so an oversized single claim would slip past the
        // ON CONFLICT predicate entirely.
        if (amount > dailyCap)
        {
            return false;
        }

        // One statement, so concurrent turns cannot interleave a read-modify-write and overshoot the
        // provider's daily allowance. Zero rows affected means the predicate rejected the claim.
        var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "AiDailyBudgets" ("DateUtc", "RequestCount", "PromptTokens", "CompletionTokens")
             VALUES ({dateUtc}, {amount}, 0, 0)
             ON CONFLICT ("DateUtc") DO UPDATE
                 SET "RequestCount" = "AiDailyBudgets"."RequestCount" + {amount}
                 WHERE "AiDailyBudgets"."RequestCount" + {amount} <= {dailyCap}
             """,
            cancellationToken).ConfigureAwait(false);

        return rowsAffected == 1;
    }

    public async Task ReleaseRequestsAsync(DateOnly dateUtc, int amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return;
        }

        // GREATEST keeps the counter from going negative if a release is ever double-applied.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE "AiDailyBudgets"
             SET "RequestCount" = GREATEST(0, "RequestCount" - {amount})
             WHERE "DateUtc" = {dateUtc}
             """,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordTokenUsageAsync(
        DateOnly dateUtc,
        int promptTokens,
        int completionTokens,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "AiDailyBudgets" ("DateUtc", "RequestCount", "PromptTokens", "CompletionTokens")
             VALUES ({dateUtc}, 0, {(long)promptTokens}, {(long)completionTokens})
             ON CONFLICT ("DateUtc") DO UPDATE
                 SET "PromptTokens" = "AiDailyBudgets"."PromptTokens" + {(long)promptTokens},
                     "CompletionTokens" = "AiDailyBudgets"."CompletionTokens" + {(long)completionTokens}
             """,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiBudgetSnapshot> ReadAsync(DateOnly dateUtc, CancellationToken cancellationToken = default)
    {
        var budget = await dbContext.Set<AiDailyBudget>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DateUtc == dateUtc, cancellationToken)
            .ConfigureAwait(false);

        return budget is null
            ? new AiBudgetSnapshot(dateUtc, 0, 0, 0)
            : new AiBudgetSnapshot(dateUtc, budget.RequestCount, budget.PromptTokens, budget.CompletionTokens);
    }
}
