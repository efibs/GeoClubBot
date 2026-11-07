using UseCases.UseCases.ClubMembers;

namespace GeoClubBot.Tests.TestDoubles;

/// <summary>
/// The real <see cref="ClubMemberSyncGate"/>, telling tests whether it is held at a given moment, so
/// a test can check that a roster read and its save both happen inside the gate.
/// </summary>
public sealed class RecordingClubMemberSyncGate : IClubMemberSyncGate
{
    private readonly ClubMemberSyncGate _inner = new();
    private int _held;

    public bool IsHeld => Volatile.Read(ref _held) > 0;

    public int Acquisitions { get; private set; }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var handle = await _inner.AcquireAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _held);
        Acquisitions++;
        return new Handle(this, handle);
    }

    private sealed class Handle(RecordingClubMemberSyncGate owner, IAsyncDisposable inner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Interlocked.Decrement(ref owner._held);
            await inner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
