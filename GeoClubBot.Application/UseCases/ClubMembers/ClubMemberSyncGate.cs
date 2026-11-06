using System.Runtime.CompilerServices;

namespace UseCases.UseCases.ClubMembers;

/// <inheritdoc cref="IClubMemberSyncGate"/>
public sealed class ClubMemberSyncGate : IClubMemberSyncGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    // The flow that acquires the gate gets a box saying "held"; everything it awaits inherits the same
    // box, so a nested acquire sees it and throws. A box rather than a plain bool because a value
    // written after an await never reaches the caller's flow, while the box's content is shared.
    private readonly AsyncLocal<StrongBox<bool>?> _heldByThisFlow = new();

    public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_heldByThisFlow.Value is { Value: true })
        {
            throw new InvalidOperationException(
                "The club member sync gate is already held by this flow; it is not re-entrant.");
        }

        // Not async on purpose: an AsyncLocal set here is visible to the caller.
        var held = new StrongBox<bool>(true);
        _heldByThisFlow.Value = held;
        return WaitAsync(held, cancellationToken);
    }

    private async Task<IAsyncDisposable> WaitAsync(StrongBox<bool> held, CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            held.Value = false;
            throw;
        }

        return new Release(_gate, held);
    }

    private sealed class Release(SemaphoreSlim gate, StrongBox<bool> held) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            // Guard against double-dispose releasing the semaphore more than once.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                held.Value = false;
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
