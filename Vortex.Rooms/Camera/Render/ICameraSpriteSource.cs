using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Camera;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// Where a photo's sprites come from: the hotel's extracted asset directory, or an allowed url.
/// </summary>
/// <remarks>
/// <para>
/// Every sprite at once, before anything is drawn, rather than one at a time while drawing. The
/// scene names all of them up front, so there is nothing to discover mid-render — and resolving
/// ahead is what keeps the canvas synchronous instead of blocking a thread on a socket in the middle
/// of a draw call.
/// </para>
/// <para>
/// Bytes, not pixels: decoding belongs to whoever draws, and keeping this boundary at "encoded image
/// or nothing" is what lets the policy — which names may be read, which hosts reached, how many
/// times — be tested with no raster library anywhere near it.
/// </para>
/// <para>
/// It does not re-decide what may be fetched. The scene decoder already classified every sprite as a
/// local key or a url that passed the operator's allowlist, and second-guessing that here would be
/// two rules to keep in agreement instead of one.
/// </para>
/// </remarks>
public interface ICameraSpriteSource
{
    /// <summary>
    /// Resolves every sprite the scene names, keyed by name.
    /// </summary>
    /// <remarks>
    /// A name that resolves to nothing is simply absent from the result: a pack one frame short
    /// leaves a hole in the photo, which beats refusing to take it.
    /// </remarks>
    /// <param name="planeAssets">
    /// The room's own surfaces — the floor and wall textures a plane's columns name. They are not
    /// sprites and are not in <paramref name="sprites"/>, but they resolve through the same pack and
    /// are drawn by the same canvas, so they are fetched in the same pass rather than discovered
    /// mid-draw.
    /// </param>
    Task<IReadOnlyDictionary<string, byte[]>> ResolveAsync(
        ImmutableArray<CameraSceneSprite> sprites,
        ImmutableArray<string> planeAssets,
        CameraRenderLimits limits,
        CancellationToken ct
    );
}
