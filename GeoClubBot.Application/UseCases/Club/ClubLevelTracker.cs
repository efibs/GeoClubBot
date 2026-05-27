using System.Collections.Concurrent;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Club;

public sealed class ClubLevelTracker : IClubLevelTracker
{
    private readonly ConcurrentDictionary<Guid, int> _lastLevels = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public async Task EnsureInitializedAsync(IClubRepository clubs, IEnumerable<Guid> clubIds, CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            foreach (var clubId in clubIds)
            {
                var club = await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
                if (club is not null)
                {
                    _lastLevels[clubId] = club.Level;
                }
            }
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public int? TryGet(Guid clubId) =>
        _lastLevels.TryGetValue(clubId, out var v) ? v : null;

    public void Set(Guid clubId, int level) => _lastLevels[clubId] = level;
}
