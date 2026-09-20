using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Camera;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms.Camera;

/// <summary>
/// Photo files under one configured root, named by minted keys.
/// </summary>
/// <remarks>
/// <para>
/// The key is generated here and never taken from the payload, which is what keeps a path out of the
/// attacker's hands. It is still validated on the way back in: a key that came from somewhere else
/// is refused rather than resolved, because the day this class is called from somewhere new is the
/// day "the key is always ours" stops being true by construction.
/// </para>
/// <para>
/// Writes land on a temporary name and are then moved into place. A photo is fetched by url the
/// moment the client is told about it, and a reader that arrives mid-write would otherwise get a
/// file that is half a photo — the same way a half-written dump once became the newest backup.
/// </para>
/// </remarks>
internal sealed class CameraPhotoStore : ICameraPhotoStore
{
    /// <summary>16 bytes of randomness, hex. Unguessable, and every character is safe in a path.</summary>
    private const int KeyBytes = 16;
    private const int KeyLength = KeyBytes * 2;

    private const string SceneExtension = ".scene.z";
    private const string PhotoExtension = ".png";

    /// <summary>What a room's thumbnail is called. Not a key, so it cannot collide with one: a
    /// minted key is thirty-two hex characters and this never is.</summary>
    private const string ThumbnailPrefix = "room-";

    private readonly string _root;
    private readonly ILogger<CameraPhotoStore> _logger;

    public CameraPhotoStore(IOptions<RoomConfig> roomConfig, ILogger<CameraPhotoStore> logger)
    {
        _logger = logger;
        // Trailing separator on purpose: without it "…/photos" is a prefix of "…/photos-public",
        // and the containment check below would wave through a sibling directory.
        _root =
            Path.GetFullPath(roomConfig.Value.CameraPhotoRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public string NewPhotoKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyBytes));

    public Task<bool> SaveSceneAsync(
        string photoKey,
        ReadOnlyMemory<byte> deflatedScene,
        CancellationToken ct
    ) => WriteAsync(photoKey, SceneExtension, deflatedScene, ct);

    public Task<bool> SavePhotoAsync(
        string photoKey,
        ReadOnlyMemory<byte> png,
        CancellationToken ct
    ) => WriteAsync(photoKey, PhotoExtension, png, ct);

    public async Task<ReadOnlyMemory<byte>> ReadPhotoAsync(string photoKey, CancellationToken ct)
    {
        if (!TryResolve(photoKey, PhotoExtension, out string? path) || !File.Exists(path))
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Camera photo {PhotoKey} could not be read.", photoKey);
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    public Task<bool> SaveRoomThumbnailAsync(
        int roomId,
        ReadOnlyMemory<byte> png,
        CancellationToken ct
    ) =>
        TryResolveThumbnail(roomId, out string? path)
            ? WriteToAsync(path, ThumbnailName(roomId), png, ct)
            : Task.FromResult(false);

    public async Task<ReadOnlyMemory<byte>> ReadRoomThumbnailAsync(int roomId, CancellationToken ct)
    {
        if (!TryResolveThumbnail(roomId, out string? path) || !File.Exists(path))
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Room {RoomId} thumbnail could not be read.", roomId);
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private Task<bool> WriteAsync(
        string photoKey,
        string extension,
        ReadOnlyMemory<byte> bytes,
        CancellationToken ct
    ) =>
        TryResolve(photoKey, extension, out string? path)
            ? WriteToAsync(path, photoKey, bytes, ct)
            : Task.FromResult(false);

    private async Task<bool> WriteToAsync(
        string path,
        string name,
        ReadOnlyMemory<byte> bytes,
        CancellationToken ct
    )
    {
        try
        {
            Directory.CreateDirectory(_root);

            // Same directory as the destination: a move across volumes is a copy, and a copy is not
            // atomic. The temporary name carries the key so a crash leaves something identifiable
            // rather than an orphan nobody dares delete.
            string temporary = path + ".partial";

            await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);

            return true;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Camera file {Name} could not be written.", name);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Camera file {Name} could not be written.", name);
            return false;
        }
    }

    public string PublicPath(string photoKey, string urlPathPrefix) =>
        !IsMintedKey(photoKey)
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{urlPathPrefix}{photoKey}.png");

    /// <summary>
    /// Turns a key into a path under the root, or refuses.
    /// </summary>
    /// <remarks>
    /// The shape check alone would do it — a key is thirty-two hex characters and none of those is a
    /// separator — but the resolved path is compared against the root as well. Two locks, because
    /// the cost is one comparison and the failure mode is writing wherever the caller likes.
    /// </remarks>
    private bool TryResolve(string photoKey, string extension, [NotNullWhen(true)] out string? path)
    {
        path = null;

        if (!IsMintedKey(photoKey))
        {
            _logger.LogWarning(
                "Camera photo key {PhotoKey} is not one this store minted.",
                photoKey
            );
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(_root, photoKey + extension));

        if (!candidate.StartsWith(_root, StringComparison.Ordinal))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    /// <summary>
    /// Turns a room id into the path of its thumbnail, or refuses.
    /// </summary>
    /// <remarks>
    /// The id is an <c>int</c> the session vouched for and is written back out through
    /// <see cref="ThumbnailName"/>, so nothing a caller supplies survives as text — a name built
    /// this way holds digits and nothing else. The containment check is kept all the same, for the
    /// same reason the minted path keeps it.
    /// </remarks>
    private bool TryResolveThumbnail(int roomId, [NotNullWhen(true)] out string? path)
    {
        path = null;

        if (roomId <= 0)
        {
            return false;
        }

        string candidate = Path.GetFullPath(
            Path.Combine(_root, ThumbnailName(roomId) + PhotoExtension)
        );

        if (!candidate.StartsWith(_root, StringComparison.Ordinal))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private static string ThumbnailName(int roomId) =>
        string.Create(CultureInfo.InvariantCulture, $"{ThumbnailPrefix}{roomId}");

    private static bool IsMintedKey(string photoKey)
    {
        if (photoKey.Length != KeyLength)
        {
            return false;
        }

        foreach (char c in photoKey)
        {
            if (!char.IsAsciiHexDigitUpper(c))
            {
                return false;
            }
        }

        return true;
    }
}
