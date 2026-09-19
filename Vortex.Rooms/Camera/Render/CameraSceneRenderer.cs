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
            // The texture first; the flat colour is what a plane gets when its columns resolve to
            // nothing — an asset pack without room content, or a client that sends no columns at
            // all. A photo without wallpaper is still a photo.
            if (
                !canvas.FillTexturedQuad(
                    plane.CornerPoints,
                    plane.Color,
                    plane.TextureColumns,
                    plane.BottomAligned,
                    IsWall(plane),
                    plane.Masks
                )
            )
            {
                canvas.FillQuad(Perimeter(plane.CornerPoints), plane.Color);
            }
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
        // The frame is the client's, and it sends it without ever naming it: getRoomPlanes unshifts a
        // background plane whose quad is exactly (0,0)-(viewport.width, viewport.height), so the
        // first plane IS the photo's dimensions.
        //
        // Measuring every plane instead does not merely guess — it guesses too large, every time.
        // getRoomPlanes only culls a plane whose four corners are off the SAME side, so a wall rising
        // above the top edge and a floor running past the right one are the ordinary case. The canvas
        // then grew to the furthest corner of the room, the background covered its own rectangle in
        // the top-left of it, and the rest was transparent: a photo with a hard straight edge where
        // the room simply stopped being painted.
        if (TryFrame(scene, out double right, out double bottom))
        {
            return new CameraViewport(Clamp(right), Clamp(bottom));
        }

        right = 0;
        bottom = 0;

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

    /// <summary>
    /// The background plane's rectangle, if the first plane looks like one.
    /// </summary>
    /// <remarks>
    /// Checked rather than assumed. "Plane zero is the frame" is true of the client that exists, and
    /// the payload is written by whoever is on the other end of the socket — so a first plane that is
    /// not anchored at the origin is treated as a room plane like any other and the scene falls back
    /// to its own extent. That costs one comparison and means a malformed payload renders badly
    /// instead of rendering into a one-pixel canvas.
    /// </remarks>
    private static bool TryFrame(CameraScene scene, out double right, out double bottom)
    {
        right = 0;
        bottom = 0;

        if (scene.Planes.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (CameraScenePoint corner in scene.Planes[0].CornerPoints)
        {
            if (corner.X < 0 || corner.Y < 0)
            {
                return false;
            }

            right = Math.Max(right, corner.X);
            bottom = Math.Max(bottom, corner.Y);
        }

        return right > 0 && bottom > 0;
    }

    /// <summary>
    /// Whether a plane is a wall, which decides only whether its texture is flipped.
    /// </summary>
    /// <remarks>
    /// Read off the texture's own name rather than any flag, because the payload carries no plane
    /// type — the client knows a wall from a floor by what it painted on it, and so does the other
    /// server that renders these. <c>landscape_</c> is a wall too: it is the window view, drawn in
    /// the same mirrored frame.
    /// </remarks>
    private static bool IsWall(CameraScenePlane plane)
    {
        foreach (ImmutableArray<string> column in plane.TextureColumns)
        {
            foreach (string asset in column)
            {
                if (
                    asset.StartsWith("wall_texture", StringComparison.Ordinal)
                    || asset.StartsWith("landscape_", StringComparison.Ordinal)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The client's four corners, in the order a polygon has to walk them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plane arrives sorted by the client's <c>SpriteDataCollector.sortQuadPoints</c>, whose two
    /// normalising swaps leave <c>points[0].x >= points[1].x</c> and <c>points[0].y >=
    /// points[2].y</c> — bottom-right, bottom-left, top-right, top-left. That is a sort order, not a
    /// path: walking it 0,1,2,3 goes along the bottom edge, across a diagonal, along the top edge and
    /// back across the other diagonal, which is a bowtie rather than a quad.
    /// </para>
    /// <para>
    /// The first real photo was every plane in the room drawn as two triangles meeting at a point. It
    /// looked like a rendering bug deep in the rasteriser and it was one transposition here. The
    /// perimeter is 0, 1, 3, 2.
    /// </para>
    /// </remarks>
    private static ImmutableArray<CameraScenePoint> Perimeter(
        ImmutableArray<CameraScenePoint> corners
    ) => corners.Length == 4 ? [corners[0], corners[1], corners[3], corners[2]] : corners;
}
