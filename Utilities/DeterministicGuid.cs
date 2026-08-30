using System.Security.Cryptography;
using System.Text;

namespace Utilities;

/// <summary>
/// Derives a stable GUID from a name, in the spirit of an RFC 4122 name-based UUID but using SHA-256.
///
/// Exists so records that are re-derived from the same upstream content land on the same identifier
/// every time. Generating a fresh <see cref="Guid.NewGuid"/> per run makes re-ingestion append
/// duplicates instead of updating in place, which in turn forces a full wipe-and-rebuild for what
/// should be an incremental update.
/// </summary>
public static class DeterministicGuid
{
    /// <summary>
    /// Namespacing prefix. Changing it changes every derived id, so it is versioned deliberately
    /// rather than left implicit.
    /// </summary>
    private const string Prefix = "geoclubbot.v1";

    /// <param name="scope">Groups ids by purpose so unrelated namespaces cannot collide.</param>
    /// <param name="name">Stable natural key of the thing being identified.</param>
    public static Guid FromName(string scope, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentNullException.ThrowIfNull(name);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Prefix}|{scope}|{name}"));

        // Take the leading 128 bits and stamp the RFC 4122 version (5) and variant bits, so the value
        // is a well-formed UUID rather than an arbitrary blob that happens to be 16 bytes.
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        // Big-endian so the textual form follows the byte order above on every platform; the default
        // constructor interprets the first three fields little-endian.
        return new Guid(bytes, bigEndian: true);
    }
}
