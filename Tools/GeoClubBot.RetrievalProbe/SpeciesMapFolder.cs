using System.Security.Cryptography;
using UseCases.OutputPorts.AI;
using Utilities;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Where <c>answer</c> and <c>map</c> keep the species maps the bot's own renderer draws: a folder
/// beside the built binary, each map under the hash of its bytes, as the bot's image relay names its
/// files. The bot uploads them to Discord from there; the probe has no Discord, so a map's link is one
/// shaped like a relayed picture's on a host that does not exist, and <see cref="PathOf"/> says where
/// the file is.
/// </summary>
public sealed class SpeciesMapFolder(string directory) : IImageRelay
{
    /// <summary>The address the maps are "served" under: reports show it, nothing fetches it.</summary>
    private const string Host = "https://species-maps.invalid";

    /// <summary>The file behind a link this folder gave out, or null for any other link.</summary>
    public string? PathOf(string? link) =>
        link is not null && RelayedImageUrl.TryReadName(link, out var name) && link.StartsWith(Host, StringComparison.Ordinal)
            ? Path.Combine(directory, name)
            : null;

    public bool IsEnabled => true;

    public Task<string> ResolveAsync(string imageUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(imageUrl);

    public async Task<Result<string>> StoreAsync(byte[] content, string? contentType, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        var extension = contentType == "image/png" ? ".png" : ".jpg";
        var name = $"{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(directory, name), content, cancellationToken).ConfigureAwait(false);

        return RelayedImageUrl.Build(Host, name);
    }

    public Task<RelayedImageContent?> ReadAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult<RelayedImageContent?>(null);

    public Task<string?> ReadAsDataUrlAsync(string imageUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<ImageRelayReachability> CheckPublicReachabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ImageRelayReachability(ImageRelayReachabilityStatus.NotConfigured, null, null));
}
