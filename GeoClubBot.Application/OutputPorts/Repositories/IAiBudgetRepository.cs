namespace UseCases.OutputPorts.Repositories;

/// <summary>
/// Guards the provider's daily free-tier request allowance.
///
/// Named <c>...Repository</c> deliberately: it is backed by a real table, and the integration-test
/// host only wires genuine EF implementations for ports whose interface name ends in "Repository" —
/// anything else under <c>OutputPorts</c> is auto-substituted, which would silently disable the
/// budget in the very tests meant to prove it holds.
/// </summary>
public interface IAiBudgetRepository
{
    /// <summary>
    /// Atomically claims <paramref name="amount"/> requests against <paramref name="dailyCap"/>,
    /// returning <c>false</c> when that would exceed the cap. Must be a single statement — callers
    /// run concurrently.
    /// </summary>
    Task<bool> TryReserveRequestsAsync(
        DateOnly dateUtc,
        int amount,
        int dailyCap,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a reservation that was never spent, e.g. when the call failed before leaving the process.</summary>
    Task ReleaseRequestsAsync(DateOnly dateUtc, int amount, CancellationToken cancellationToken = default);

    /// <summary>Records token counts after a call. Reporting only; never gates a request.</summary>
    Task RecordTokenUsageAsync(
        DateOnly dateUtc,
        int promptTokens,
        int completionTokens,
        CancellationToken cancellationToken = default);

    Task<AiBudgetSnapshot> ReadAsync(DateOnly dateUtc, CancellationToken cancellationToken = default);
}

public sealed record AiBudgetSnapshot(
    DateOnly DateUtc,
    int RequestCount,
    long PromptTokens,
    long CompletionTokens);
