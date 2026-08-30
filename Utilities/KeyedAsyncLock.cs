namespace Utilities;

/// <summary>
/// Mutual exclusion per key, so unrelated work runs in parallel while work sharing a key serialises.
///
/// Written for conversations: two rapid replies in the same branch would otherwise both read the
/// history before either wrote its turn, producing two sibling answers and spending the request
/// budget twice. Different conversations have no reason to wait on each other, which is why a single
/// global lock — the previous design — was the wrong shape.
///
/// Entries are reference-counted and removed once idle, so a long-lived instance does not accumulate
/// a semaphore per conversation ever seen.
/// </summary>
public sealed class KeyedAsyncLock<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly Lock _gate = new();

    /// <summary>Waits for exclusive access to <paramref name="key"/>. Dispose the result to release.</summary>
    public async Task<IDisposable> AcquireAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            // Counted before waiting so a concurrent release cannot evict the entry we are about to
            // queue on, which would let two callers hold different semaphores for the same key.
            entry.WaiterCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    /// <summary>Number of keys currently held or waited on. Exposed for tests.</summary>
    public int TrackedKeyCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    private void ReleaseReference(TKey key, Entry entry)
    {
        lock (_gate)
        {
            if (--entry.WaiterCount == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int WaiterCount { get; set; }
    }

    private sealed class Releaser(KeyedAsyncLock<TKey> owner, TKey key, Entry entry) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            // Order matters: hand the slot to the next waiter before dropping our reference, so the
            // entry is not disposed while someone is still queued on it.
            entry.Semaphore.Release();
            owner.ReleaseReference(key, entry);
        }
    }
}
