namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Serializes everything that puts a private channel into a category. The decision "this category has
/// a free slot" and the move into it must not interleave with another one, or two members could take
/// the last slot and Discord would refuse the second (or two overflow categories would be created).
/// Not re-entrant: a handler holding it calls <see cref="MemberPrivateChannelManager"/> directly, never
/// another locking command.
/// </summary>
internal static class PrivateChannelPlacementLock
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            Semaphore.Release();
        }
    }
}
