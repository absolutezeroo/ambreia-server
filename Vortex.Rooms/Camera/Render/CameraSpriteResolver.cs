using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Camera;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// Sprites from the hotel's extracted asset directory, and from the urls an operator allowed.
/// </summary>
/// <remarks>
/// <para>
/// The local branch is a dictionary lookup made of a directory: a name is a key, joined to the root
/// and checked to still be under it. Nothing walks, nothing globs, and a name that escapes is
/// refused rather than repaired — the same shape the photo store uses for its own files.
/// </para>
/// <para>
/// The network branch is the one that needs a leash. A scene may name dozens of external images and
/// each is a request this hotel makes on the payload's say-so; the allowlist decided <em>where</em>,
/// and the budget here decides <em>how many</em> and for how long, so one photo cannot become a
/// burst against the asset host or a render that never ends because a host stopped answering.
/// </para>
/// </remarks>
internal sealed class CameraSpriteResolver(
    string spriteRoot,
    HttpClient http,
    NitroSpriteLibrary library,
    ILogger logger
) : ICameraSpriteSource, IDisposable
{
    /// <summary>Disposes the client this was given. Harmless when a caller owns it too: disposing an
    /// <c>HttpClient</c> twice does nothing the second time.</summary>
    public void Dispose() => http.Dispose();

    /// <summary>
    /// The client the hotel uses for camera fetches.
    /// </summary>
    /// <remarks>
    /// One long-lived instance rather than one per photo: a new <c>HttpClient</c> per render leaks
    /// sockets in TIME_WAIT until the machine runs out of them, which is the oldest trap in this
    /// library. <c>PooledConnectionLifetime</c> is what a long-lived one still needs — without it a
    /// pooled connection outlives the DNS record behind it, and the asset host moving becomes a
    /// camera that fetches from wherever it used to be until the process restarts.
    /// </remarks>
    public static HttpClient CreateHttpClient()
    {
        SocketsHttpHandler? handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            // The allowlist matched the url the payload named. A redirect is a second address that
            // was never checked, so it is not followed.
            AllowAutoRedirect = false,
        };

        try
        {
            HttpClient client = new(handler, disposeHandler: true);

            // Ownership has moved to the client; the finally below must not take it back.
            handler = null;

            return client;
        }
        finally
        {
            handler?.Dispose();
        }
    }

    private readonly string _root =
        Path.GetFullPath(spriteRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    public async Task<IReadOnlyDictionary<string, byte[]>> ResolveAsync(
        ImmutableArray<CameraSceneSprite> sprites,
        CameraRenderLimits limits,
        CancellationToken ct
    )
    {
        Dictionary<string, byte[]> resolved = new(StringComparer.Ordinal);
        int fetches = 0;
        int bundles = 0;

        // Distinct: a tiled floor names one asset a hundred times, and reading it a hundred times
        // would be the whole cost of a render.
        foreach (CameraSceneSprite sprite in sprites)
        {
            if (resolved.ContainsKey(sprite.Name))
            {
                continue;
            }

            byte[]? bytes;

            if (sprite.Source == CameraSpriteSource.LocalAsset)
            {
                // The directory first, the hotel's own .nitro pack second. That order is the useful
                // one: the pack is the source of truth and needs no maintenance, and a PNG dropped
                // in the directory is then an operator's override of one asset rather than the only
                // way to have any.
                bytes = ReadLocal(sprite.Name);

                if (bytes is null && bundles < limits.MaxBundleFetches)
                {
                    bytes = await library
                        .GetSpriteAsync(sprite.Name, limits, ct)
                        .ConfigureAwait(false);

                    // Counted per distinct name rather than per request, because the library caches
                    // and most names in a room resolve to a bundle it already holds. A furniture
                    // name is refused against the catalogue without any request at all, so what this
                    // really bounds is the one family that cannot be checked first: a pet's bundle
                    // name is simply the head of its sprite name, which makes any invented name an
                    // address.
                    bundles++;
                }
            }
            else if (fetches < limits.MaxExternalFetches)
            {
                fetches++;
                bytes = await FetchAsync(sprite.Name, limits, ct).ConfigureAwait(false);
            }
            else
            {
                // Past the budget the photo simply loses its remaining badges. The client does the
                // same to its own past thirty external images, so this is a shape it already makes.
                logger.LogDebug(
                    "Camera photo passed its external image budget of {Budget}; {Name} was not fetched.",
                    limits.MaxExternalFetches,
                    sprite.Name
                );
                continue;
            }

            if (bytes is { Length: > 0 })
            {
                resolved[sprite.Name] = bytes;
            }
        }

        return resolved;
    }

    private byte[]? ReadLocal(string key)
    {
        string candidate = Path.GetFullPath(Path.Combine(_root, key + ".png"));

        // The decoder already refuses a key with a separator in it. This is the second lock, and it
        // is the one that still holds the day somebody joins a name here without asking.
        if (!candidate.StartsWith(_root, StringComparison.Ordinal))
        {
            logger.LogWarning("Camera sprite {Key} resolved outside the asset directory.", key);
            return null;
        }

        try
        {
            return File.Exists(candidate) ? File.ReadAllBytes(candidate) : null;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Camera sprite {Key} could not be read.", key);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Camera sprite {Key} could not be read.", key);
            return null;
        }
    }

    private async Task<byte[]?> FetchAsync(
        string url,
        CameraRenderLimits limits,
        CancellationToken ct
    )
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limits.ExternalTimeout);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token
                )
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // Bounded before it is read, not measured after: a host that answers with a stream of
            // zeroes forever is a host this hotel asked, and Content-Length is the sender's claim.
            if (response.Content.Headers.ContentLength > limits.MaxExternalBytes)
            {
                return null;
            }

            await using Stream body = await response
                .Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);

            return await ReadBoundedAsync(body, limits.MaxExternalBytes, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Camera external image {Url} could not be fetched.", url);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("Camera external image {Url} timed out.", url);
            return null;
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream body,
        int maxBytes,
        CancellationToken ct
    )
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];

        while (true)
        {
            int read = await body.ReadAsync(chunk, ct).ConfigureAwait(false);

            if (read == 0)
            {
                return buffer.Length == 0 ? null : buffer.ToArray();
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk, 0, read);
        }
    }
}
