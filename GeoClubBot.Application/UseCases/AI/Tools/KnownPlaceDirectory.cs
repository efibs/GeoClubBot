using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// The known towns and regions as a <see cref="PlaceIndex"/>, built once per list the directory returns
/// and kept: the list is some 40,000 places, read once per process, and indexing it takes a moment a
/// question should not wait for twice. A singleton for that reason.
/// </summary>
public sealed class KnownPlaceDirectory(IPlaceDirectory directory)
{
    private readonly Lock _lock = new();
    private (IReadOnlyList<KnownPlace> Source, PlaceIndex Index)? _built;

    /// <summary>The index, or <see cref="PlaceIndex.Empty"/> while the places cannot be read. Never throws.</summary>
    public async Task<PlaceIndex> ReadAsync(CancellationToken cancellationToken = default)
    {
        var places = await directory.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (places.Count == 0)
        {
            return PlaceIndex.Empty;
        }

        lock (_lock)
        {
            if (_built is { } built && ReferenceEquals(built.Source, places))
            {
                return built.Index;
            }
        }

        var index = PlaceIndex.Build(places);
        lock (_lock)
        {
            _built = (places, index);
        }

        return index;
    }
}
