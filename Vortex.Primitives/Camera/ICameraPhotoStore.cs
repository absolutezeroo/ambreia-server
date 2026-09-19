using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Camera;

/// <summary>
/// Where a photo's bytes live on disk.
/// </summary>
/// <remarks>
/// Two kinds of file share one key: the scene as it arrived, kept so the rasteriser can be run — or
/// re-run — without the player being present, and the rendered PNG the client fetches. Both are
/// named by a key this server minted; nothing from the payload reaches a path.
/// </remarks>
public interface ICameraPhotoStore
{
    /// <summary>A fresh, unguessable key. Photos are fetched by url with no further authorisation,
    /// so the key is the only thing standing between a private shot and anyone who can count.</summary>
    string NewPhotoKey();

    /// <summary>
    /// Writes the compressed scene under <paramref name="photoKey"/>, replacing any previous one.
    /// </summary>
    /// <returns>False when the key is not one this store minted, or the write failed.</returns>
    Task<bool> SaveSceneAsync(
        string photoKey,
        ReadOnlyMemory<byte> deflatedScene,
        CancellationToken ct
    );

    /// <summary>
    /// Writes the rendered PNG under <paramref name="photoKey"/>, replacing any previous one.
    /// </summary>
    /// <returns>False when the key is not one this store minted, or the write failed.</returns>
    Task<bool> SavePhotoAsync(string photoKey, ReadOnlyMemory<byte> png, CancellationToken ct);

    /// <summary>
    /// The rendered PNG, or empty when there is none yet.
    /// </summary>
    /// <remarks>
    /// The route that serves photos goes through here rather than mapping a url onto the filesystem
    /// itself: the key is validated in one place, and nothing outside this class ever turns a
    /// request into a path.
    /// </remarks>
    Task<ReadOnlyMemory<byte>> ReadPhotoAsync(string photoKey, CancellationToken ct);

    /// <summary>The path the client is told, relative to its own <c>stories.image_url_base</c>.</summary>
    string PublicPath(string photoKey, string urlPathPrefix);
}
