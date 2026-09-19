using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Camera;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// Resolve every sprite, draw the scene, store the PNG.
/// </summary>
/// <remarks>
/// <para>
/// The three steps are separate classes and this is the only place that knows they go together.
/// </para>
/// <para>
/// <b>Nothing here bounds how many photos are drawn at once, and that is deliberate.</b> A gate was
/// written and taken out: this runs inside a grain turn, so a contended primitive here is a grain
/// waiting on a lock, which is the exact shape <c>RoomGrainConcurrencyTests</c> forbids across this
/// project — "either redundant or a deadlock waiting for the room tick". What holds today is the
/// per-player rate limit and the fact that a render is short. That is a real ceiling, not a
/// guarantee: enough players at their allowance is enough concurrent renders to matter, and the
/// answer when it does is to move drawing off the turn entirely — a queue drained by its own
/// worker — rather than to make a grain wait.
/// </para>
/// <para>
/// Nothing here throws on a bad photo. The scene arrived hostile and was made safe upstream; what is
/// left to go wrong is a full disk or an asset host having a bad day, and both are a false, a log
/// line, and an empty url for the player.
/// </para>
/// </remarks>
internal sealed class RoomPhotoRenderer(
    ICameraSpriteSource sprites,
    ICameraPhotoStore store,
    ILogger<RoomPhotoRenderer> logger
) : IRoomPhotoRenderer
{
    public async Task<bool> RenderAsync(
        CameraScene scene,
        string photoKey,
        CameraRenderLimits limits,
        CancellationToken ct
    )
    {
        IReadOnlyDictionary<string, byte[]> resolved = await sprites
            .ResolveAsync(scene.Sprites, limits, ct)
            .ConfigureAwait(false);

        byte[] png = new CameraSceneRenderer(limits.MaxDimension).Render(
            scene,
            viewport => new SkiaCameraCanvas(viewport, name => Lookup(resolved, name))
        );

        if (!await store.SavePhotoAsync(photoKey, png, ct).ConfigureAwait(false))
        {
            logger.LogError("Camera photo {PhotoKey} was drawn and could not be stored.", photoKey);
            return false;
        }

        logger.LogDebug(
            "Camera photo {PhotoKey}: {Planes} plane(s), {Sprites} sprite(s), {Resolved} resolved, {Bytes} bytes.",
            photoKey,
            scene.Planes.Length,
            scene.Sprites.Length,
            resolved.Count,
            png.Length
        );

        return true;
    }

    private static byte[]? Lookup(IReadOnlyDictionary<string, byte[]> resolved, string name) =>
        resolved.TryGetValue(name, out byte[]? bytes) ? bytes : null;
}
