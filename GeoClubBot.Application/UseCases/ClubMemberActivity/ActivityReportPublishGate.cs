namespace UseCases.UseCases.ClubMemberActivity;

/// <inheritdoc cref="IActivityReportPublishGate"/>
public sealed class ActivityReportPublishGate : IActivityReportPublishGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Release(_gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            // Guard against double-dispose releasing the semaphore more than once.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
