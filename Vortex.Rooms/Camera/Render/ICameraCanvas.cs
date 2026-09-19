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
