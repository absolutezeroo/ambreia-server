using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using SkiaSharp;
using Vortex.Primitives.Camera;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// The one class that draws.
/// </summary>
/// <remarks>
/// <para>
/// Everything above it decides; this puts the pixels down and encodes them. Keeping the raster
/// library to one file is deliberate: a photo's correctness is mostly about order and transforms,
/// which are asserted elsewhere against a recording canvas, and what is left here is thin enough to
/// read.
/// </para>
/// <para>
/// Sprites are resolved through the <see cref="ICameraSpriteSource"/> handed in and cached for the
/// life of this canvas. One sprite is drawn many times in a photo — a tiled floor, a repeated avatar
/// part — and decoding it per draw would be most of the cost of a render. A name that resolves to
/// nothing is cached as nothing too, so a missing asset is looked for once.
/// </para>
/// </remarks>
internal sealed class SkiaCameraCanvas : ICameraCanvas
{
    private readonly SKSurface _surface;
    private readonly Func<string, byte[]?> _readSprite;
    private readonly Dictionary<string, SKImage?> _sprites = new(StringComparer.Ordinal);

    private static readonly SKSamplingOptions NearestNeighbour = new(SKFilterMode.Nearest);

    public SkiaCameraCanvas(CameraViewport viewport, Func<string, byte[]?> readSprite)
    {
        _readSprite = readSprite;
        _surface = SKSurface.Create(
            new SKImageInfo(
                viewport.Width,
                viewport.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Premul
            )
        );
    }

    public void Clear(int argb) => _surface.Canvas.Clear(new SKColor((uint)argb));

    public void FillQuad(ImmutableArray<CameraScenePoint> corners, int argb)
    {
        using SKPathBuilder builder = new();

        builder.MoveTo((float)corners[0].X, (float)corners[0].Y);

        for (int i = 1; i < corners.Length; i++)
        {
            builder.LineTo((float)corners[i].X, (float)corners[i].Y);
        }

        builder.Close();

        using SKPath path = builder.Detach();

        using SKPaint paint = new()
        {
            // The scene's plane colour carries no alpha of its own — it is a room surface, not a
            // decal — so it is drawn opaque rather than inheriting whatever the top byte happened
            // to be.
            Color = new SKColor((uint)argb).WithAlpha(byte.MaxValue),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        _surface.Canvas.DrawPath(path, paint);
    }

    public bool DrawSprite(string name, CameraSpriteDraw draw)
    {
        SKImage? image = Resolve(name);

        if (image is null)
        {
            return false;
        }

        using SKPaint paint = new()
        {
            Color = SKColors.White.WithAlpha((byte)Math.Clamp(draw.Alpha, 0, 255)),
            ColorFilter = TintFor(draw.Color),
            BlendMode = BlendFor(draw.BlendMode),
            IsAntialias = false,
        };

        int saved = _surface.Canvas.Save();

        // Translate, then shear, then flip — in that order, because each is expressed relative to
        // the sprite's own top-left and not to the surface.
        _surface.Canvas.Translate((float)draw.X, (float)draw.Y);

        if (draw.Skew != 0)
        {
            _surface.Canvas.Skew((float)draw.Skew, 0);
        }

        if (draw.FlipH)
        {
            _surface.Canvas.Scale(-1, 1);
            _surface.Canvas.Translate(-image.Width, 0);
        }

        // Nearest, never bilinear: these are pixel-art sprites drawn at their own scale, and
        // smoothing them is how a Habbo photo stops looking like Habbo.
        _surface.Canvas.DrawImage(image, new SKPoint(0, 0), NearestNeighbour, paint);
        _surface.Canvas.RestoreToCount(saved);

        return true;
    }

    public byte[] EncodePng()
    {
        using SKImage snapshot = _surface.Snapshot();
        using SKData data = snapshot.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    public void Dispose()
    {
        foreach (SKImage? sprite in _sprites.Values)
        {
            sprite?.Dispose();
        }

        _sprites.Clear();
        _surface.Dispose();
    }

    private SKImage? Resolve(string name)
    {
        if (_sprites.TryGetValue(name, out SKImage? cached))
        {
            return cached;
        }

        byte[]? encoded = _readSprite(name);
        SKImage? image = encoded is null or { Length: 0 } ? null : SKImage.FromEncodedData(encoded);

        // Nulls are cached as well: an asset pack that is one frame short should be looked in once,
        // not once per sprite that names the missing frame.
        _sprites[name] = image;

        return image;
    }

    /// <summary>
    /// The client's tint: the sprite's own pixels multiplied by a colour.
    /// </summary>
    private static SKColorFilter? TintFor(int? color) =>
        color is null
            ? null
            : SKColorFilter.CreateBlendMode(
                new SKColor((uint)color.Value).WithAlpha(byte.MaxValue),
                SKBlendMode.Modulate
            );

    /// <summary>
    /// The blend names the client writes. Anything else draws normally rather than guessing — an
    /// unknown name is a client this server has not been taught, and a wrong blend is louder than
    /// no blend.
    /// </summary>
    private static SKBlendMode BlendFor(string? blendMode) =>
        blendMode switch
        {
            "add" => SKBlendMode.Plus,
            "multiply" => SKBlendMode.Multiply,
            "screen" => SKBlendMode.Screen,
            "overlay" => SKBlendMode.Overlay,
            "darken" => SKBlendMode.Darken,
            "lighten" => SKBlendMode.Lighten,
            "difference" => SKBlendMode.Difference,
            "hardlight" => SKBlendMode.HardLight,
            _ => SKBlendMode.SrcOver,
        };
}
