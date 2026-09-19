using System;
using System.Collections.Immutable;
using Vortex.Primitives.Camera;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// The only surface in the camera that knows how pixels are put down.
/// </summary>
/// <remarks>
/// <para>
/// Everything about <em>what</em> a photo contains — which planes, in what order, which sprites over
/// them, where, tinted how — lives in <see cref="CameraSceneRenderer"/> and is decided against this
/// interface. That is what makes draw order, transforms and tints assertable without rendering a
/// single pixel, and it keeps the raster library behind one thin implementation instead of running
/// through the whole feature.
/// </para>
/// <para>
/// A sprite is named, not handed over. Decoding and caching the bytes an
/// <see cref="ICameraSpriteSource"/> produced is the implementation's business — one sprite is drawn
/// many times in a photo (a tiled floor, a repeated avatar part) and decoding it once per draw would
/// be the whole cost of a render. Keeping the decoded form on this side is also what lets everything
/// above it stay free of the raster library's types.
/// </para>
/// <para>
/// Deliberately small. It grows when the scene asks for something it cannot express, not in
/// anticipation.
/// </para>
/// </remarks>
public interface ICameraCanvas : IDisposable
{
    /// <summary>Fills the whole surface, before anything is drawn on it.</summary>
    void Clear(int argb);

    /// <summary>
    /// Fills the quad a room plane occupies. The four points are the scene's own, in its order.
    /// </summary>
    void FillQuad(ImmutableArray<CameraScenePoint> corners, int argb);

    /// <summary>
    /// Fills a room plane with its own texture instead of a flat colour.
    /// </summary>
    /// <param name="sortedCorners">
    /// The scene's four corners <b>as they arrived</b>, not the perimeter order
    /// <see cref="FillQuad"/> takes. The texture is mapped onto the plane by an affine basis read
    /// straight off them — corner 0 is the origin, corner 1 gives the width axis, corner 2 the
    /// height axis — so re-ordering them here would be re-ordering the basis.
    /// </param>
    /// <param name="columns">
    /// One entry per texture column, each a stack of asset names from bottom to top. The client
    /// sends no coordinates with them because none are needed: a column's position is its rank, and
    /// the list repeats until the plane is covered.
    /// </param>
    /// <param name="bottomAligned">
    /// Whether a column that does not fill the plane sits at the bottom of it rather than the top —
    /// a half-height wall panel against a floor tile.
    /// </param>
    /// <param name="mirrored">
    /// Flips the finished texture on both axes. Wall art is authored with its baseboard at the
    /// bottom of the source image and the client paints walls in a mirrored frame, so without this a
    /// wall comes out upside-down and any asymmetric pattern comes out reversed.
    /// </param>
    /// <returns>
    /// False when the plane could not be textured — no column resolved, or the plane has no area.
    /// The caller then fills it flat, which is what a photo did before textures were drawn at all.
    /// </returns>
    bool FillTexturedQuad(
        ImmutableArray<CameraScenePoint> sortedCorners,
        int argb,
        ImmutableArray<ImmutableArray<string>> columns,
        bool bottomAligned,
        bool mirrored
    );

    /// <summary>
    /// Draws the sprite called <paramref name="name"/>.
    /// </summary>
    /// <returns>
    /// False when nothing could be resolved under that name. A missing sprite is a hole in the
    /// photo, not a failed render: an asset pack that is one frame short should still take pictures.
    /// </returns>
    bool DrawSprite(string name, CameraSpriteDraw draw);

    /// <summary>Encodes what has been drawn as a PNG.</summary>
    byte[] EncodePng();
}
