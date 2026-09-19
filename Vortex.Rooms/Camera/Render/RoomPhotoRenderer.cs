using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
        // The room's surfaces are named inside the planes, not among the sprites, so they have to be
        // gathered before anything is resolved -- distinct, because one wallpaper covers every wall.
        ImmutableArray<string> planeAssets =
        [
            .. scene
                .Planes.SelectMany(plane => plane.TextureColumns)
                .SelectMany(column => column)
                .Concat(
                    scene.Planes.SelectMany(plane => plane.Masks).Select(mask => mask.AssetName)
                )
                .Where(asset => asset.Length != 0)
                .Distinct(StringComparer.Ordinal),
        ];

        IReadOnlyDictionary<string, byte[]> resolved = await sprites
            .ResolveAsync(scene.Sprites, planeAssets, limits, ct)
            .ConfigureAwait(false);

        CameraSceneRenderer renderer = new(limits.MaxDimension);
        CameraViewport viewport = renderer.ViewportFor(scene);

        byte[] png = renderer.Render(
            scene,
            _ => new SkiaCameraCanvas(viewport, name => Lookup(resolved, name))
        );

        if (!await store.SavePhotoAsync(photoKey, png, ct).ConfigureAwait(false))
        {
            logger.LogError("Camera photo {PhotoKey} was drawn and could not be stored.", photoKey);
            return false;
        }

        // Information, not Debug, and it prints the scene rather than a count.
        //
        // Four rounds of this feature were spent reasoning backwards from the finished photo —
        // measuring colour regions, comparing silhouettes — to guess what the payload had said. That
        // is the wrong end. The payload is the only ground truth, the server is the only thing that
        // sees it, and one line of it here settles in seconds what an afternoon of looking at
        // pictures could not.
        // The names nothing could draw, which is the difference between "the client never sent it"
        // and "it was sent and I could not find it".
        string[] missing =
        [
            .. scene
                .Sprites.Select(sprite => sprite.Name)
                .Concat(planeAssets)
                .Distinct(StringComparer.Ordinal)
                .Where(name => !resolved.ContainsKey(name))
                .ToArray(),
        ];

        // Sprites and plane assets counted apart. They were counted together and the subtraction ran
        // the resolved total -- which holds both -- against the sprite names alone, so the first real
        // photo reported "4 sprites of which 7 resolved, -3 not". A diagnostic that prints a negative
        // is worse than none: it is the number somebody reaches for to decide whose bug this is.
        logger.LogInformation(
            "Camera photo {PhotoKey}: viewport {Width}x{Height}, {Planes} plane(s) naming "
                + "{PlaneAssets} asset(s), {Sprites} sprite(s), {Missing} of both unresolved, {Bytes} bytes.",
            photoKey,
            viewport.Width,
            viewport.Height,
            scene.Planes.Length,
            planeAssets.Length,
            scene.Sprites.Length,
            missing.Length,
            png.Length
        );

        if (missing.Length != 0)
        {
            logger.LogInformation(
                "Camera photo {PhotoKey}: unresolved {Missing}.",
                photoKey,
                string.Join(", ", missing.Take(12))
            );
        }

        // The first planes, in the order they are painted, with the corners the client actually
        // sent. This is what a local reproduction needs and cannot otherwise get.
        foreach (CameraScenePlane plane in scene.Planes.Take(6))
        {
            logger.LogInformation(
                "Camera photo {PhotoKey}: plane z={Z} colour=#{Colour:X6} corners {Corners}.",
                photoKey,
                plane.Z,
                plane.Color & 0xFFFFFF,
                string.Join(
                    " ",
                    plane.CornerPoints.Select(corner =>
                        FormattableString.Invariant($"({corner.X:0.#},{corner.Y:0.#})")
                    )
                )
            );
        }

        return true;
    }

    private static byte[]? Lookup(IReadOnlyDictionary<string, byte[]> resolved, string name) =>
        resolved.TryGetValue(name, out byte[]? bytes) ? bytes : null;
}
