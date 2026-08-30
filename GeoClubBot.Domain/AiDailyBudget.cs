namespace Entities;

/// <summary>
/// One row per UTC day counting upstream AI requests, so the bot cannot blow through the provider's
/// daily free-tier allowance.
///
/// The counter is incremented through a single atomic SQL statement rather than through this entity's
/// setters — see <c>IAiBudgetRepository.TryReserveRequestsAsync</c>. Concurrent Discord messages would
/// otherwise race on read-modify-write and overshoot the cap, which upstream answers with sustained
/// HTTP 429s.
/// </summary>
public class AiDailyBudget : BaseEntity
{
    public DateOnly DateUtc { get; private set; }

    public int RequestCount { get; private set; }

    public long PromptTokens { get; private set; }

    public long CompletionTokens { get; private set; }

    public static AiDailyBudget Create(DateOnly dateUtc) => new()
    {
        DateUtc = dateUtc
    };

    private AiDailyBudget()
    {
    }

    public override string ToString() => $"{DateUtc:yyyy-MM-dd}: {RequestCount} request(s)";
}
