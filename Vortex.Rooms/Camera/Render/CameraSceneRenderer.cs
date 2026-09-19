using System;
using System.Collections.Immutable;
using Vortex.Primitives.Camera;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// Decides what a photo contains and in what order, against an <see cref="ICameraCanvas"/>.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and that is the point: draw order, the viewport it derives, the transforms it hands down
/// and the tints it resolves are all assertable against a recording canvas, with no raster library
/// and no pixels. The reference emulator does this the other way round — its renderer talks to
/// <c>java.awt</c> directly across two thousand lines — and none of those decisions can be checked
/// there without comparing images.
/// </para>
/// <para>
/// Planes first, then sprites, each in the order the scene lists them. The client composited them in
/// exactly that order before writing the payload, so honouring it is both simpler than re-sorting
/// and more faithful than any depth rule this side could invent.
/// </para>
/// </remarks>
public sealed class CameraSceneRenderer(int maxDimension)
{
    /// <summary>Fully transparent. A room photo is not rectangular at its corners.</summary>
    private const int Transparent = 0x00000000;

    /// <summary>
    /// Draws <paramref name="scene"/> and returns the encoded PNG.
    /// </summary>
    public byte[] Render(CameraScene scene, Func<CameraViewport, ICameraCanvas> canvasFactory)
    {
        CameraViewport viewport = ViewportFor(scene);

        using ICameraCanvas canvas = canvasFactory(viewport);

        canvas.Clear(Transparent);

        foreach (CameraScenePlane plane in scene.Planes)
        {
            // Colour only, for now. The masks a plane carries and the texture columns painted onto
            // it are read and validated by the decoder and not yet drawn, so a photo today has the
            // room's shape and its flat colours: recognisably the room, without its wallpaper or its
            // doorway cut out.
            canvas.FillQuad(plane.CornerPoints, plane.Color);
        }

        foreach (CameraSceneSprite sprite in scene.Sprites)
        {
            canvas.DrawSprite(
                sprite.Name,
                new CameraSpriteDraw
                {
                    X = sprite.X,
                    Y = sprite.Y,
                    Alpha = sprite.Alpha,
                    FlipH = sprite.FlipH,
                    Skew = sprite.Skew,
                    Color = sprite.Color,
                    BlendMode = sprite.BlendMode,
                }
            );
        }

        return canvas.EncodePng();
    }

    /// <summary>
    /// How big the photo is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The payload does not say. The client builds a background plane spanning its whole viewport
    /// before anything else, so the planes' own extent is the viewport — and taking the extent of
    /// all of them rather than trusting one to be first survives a scene that has no background
    /// plane at all.
    /// </para>
    /// <para>
    /// Clamped, because nothing upstream bounds a coordinate. A corner at a billion is a valid
    /// number in a valid document, and allocating the surface it asks for is how a photo becomes an
    /// out-of-memory. The ceiling is the operator's.
    /// </para>
    /// </remarks>
    public CameraViewport ViewportFor(CameraScene scene)
    {
        double right = 0;
        double bottom = 0;

        foreach (CameraScenePlane plane in scene.Planes)
        {
            foreach (CameraScenePoint corner in plane.CornerPoints)
            {
                right = Math.Max(right, corner.X);
                bottom = Math.Max(bottom, corner.Y);
            }
        }

        // A scene whose planes are all off to the left still has to produce something drawable, and
        // one pixel is a photo nobody wants rather than a crash.
        int width = Clamp(right);
        int height = Clamp(bottom);

        return new CameraViewport(width, height);
    }

    private int Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp((int)Math.Ceiling(value), 1, maxDimension) : 1;
}
