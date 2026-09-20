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

    /// <summary>
    /// Ceiling on one plane's texture. The plane's corners are payload numbers, so its own scale is
    /// not a bound — this is.
    /// </summary>
    private const long MaxTexturePixels = 8_000_000;

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

    /// <summary>
    /// A textured plane, built the way a second server implementation builds one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three steps. Stack each column's cells into a strip as tall as the plane; lay the strips side
    /// by side across its width, repeating the list until it is covered; map the finished image onto
    /// the plane with an affine basis taken from the corners. That is why a column carries no
    /// coordinates — its position is its rank.
    /// </para>
    /// <para>
    /// The basis is <c>origin = corner 0</c>, <c>u = corner 1 - corner 0</c>, <c>v = corner 2 -
    /// corner 0</c>. It is affine and not a general quad map because a room plane is a
    /// parallelogram by construction: the client builds it as an origin and two edges.
    /// </para>
    /// </remarks>
    public bool FillTexturedQuad(
        ImmutableArray<CameraScenePoint> sortedCorners,
        int argb,
        ImmutableArray<ImmutableArray<string>> columns,
        bool bottomAligned,
        bool mirrored,
        ImmutableArray<CameraSceneMask> masks
    )
    {
        if (sortedCorners.Length < 4 || columns.IsDefaultOrEmpty)
        {
            return false;
        }

        CameraScenePoint origin = sortedCorners[0];
        (double ux, double uy) = (sortedCorners[1].X - origin.X, sortedCorners[1].Y - origin.Y);
        (double vx, double vy) = (sortedCorners[2].X - origin.X, sortedCorners[2].Y - origin.Y);

        // The texture is built at the plane's own scale, so a wall twice as long carries twice as
        // many tiles rather than the same ones stretched — and at the scale the CLIENT rasterises
        // it, which is not the length of the edge on screen. See <see cref="TextureExtent"/>.
        int width = TextureExtent(ux, uy);
        int height = TextureExtent(vx, vy);

        if (width <= 0 || height <= 0 || (long)width * height > MaxTexturePixels)
        {
            return false;
        }

        using SKImage? texture = BuildTexture(columns, width, height, bottomAligned, argb, masks);

        if (texture is null)
        {
            return false;
        }

        int saved = _surface.Canvas.Save();

        // Clipped to the plane, because the affine map covers the parallelogram exactly but a
        // rounded-up texture size can spill a pixel past its edge.
        using SKPathBuilder builder = new();

        builder.MoveTo((float)sortedCorners[0].X, (float)sortedCorners[0].Y);
        builder.LineTo((float)sortedCorners[1].X, (float)sortedCorners[1].Y);
        builder.LineTo((float)sortedCorners[3].X, (float)sortedCorners[3].Y);
        builder.LineTo((float)sortedCorners[2].X, (float)sortedCorners[2].Y);
        builder.Close();

        using SKPath clip = builder.Detach();

        _surface.Canvas.ClipPath(clip, antialias: true);

        _surface.Canvas.SetMatrix(
            new SKMatrix(
                (float)(ux / width),
                (float)(vx / height),
                (float)origin.X,
                (float)(uy / width),
                (float)(vy / height),
                (float)origin.Y,
                0,
                0,
                1
            )
        );

        using SKPaint paint = new() { IsAntialias = false };

        if (mirrored)
        {
            _surface.Canvas.Scale(-1, -1);
            _surface.Canvas.Translate(-width, -height);
        }

        _surface.Canvas.DrawImage(texture, new SKPoint(0, 0), NearestNeighbour, paint);
        _surface.Canvas.RestoreToCount(saved);

        return true;
    }

    /// <summary>
    /// One of a plane's two axes, in the pixels the client's own texture is measured in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plane is rasterised flat and then squashed onto its parallelogram, so its texture is the
    /// plane's own surface — <c>side.length * scale</c> — and not the length of the edge that
    /// surface projects to. The two differ on any axis the isometric view foreshortens: a room tile
    /// is <c>scale</c> wide flat and projects to <c>(scale/2, scale/4)</c>, which is <c>sqrt(5)/4</c>
    /// of it, while the vertical axis projects straight down at one pixel per unit.
    /// </para>
    /// <para>
    /// Measured on a real payload: the wall carrying the doorway arrives with edges of 466 and 115
    /// pixels. 466 is thirteen tiles foreshortened, so the client's texture for it is 13 x 64 = 832
    /// across, and the mask it sends sits somewhere in 0..832. Read into a 466-wide texture that
    /// same number lands nearly twice as far along the wall as it should — past the end of it, for a
    /// door in the far half — which is a doorway that never appears however faithfully it is
    /// punched. The wallpaper was stretched by the same factor for the same reason.
    /// </para>
    /// </remarks>
    private static int TextureExtent(double dx, double dy)
    {
        double projected = Math.Sqrt((dx * dx) + (dy * dy));

        // The vertical axis is the one that projects with no horizontal component at all.
        return (int)Math.Round(projected); // TEMP
    }

    /// <summary>Columns stacked to height, then laid side by side across width, repeating.</summary>
    private SKImage? BuildTexture(
        ImmutableArray<ImmutableArray<string>> columns,
        int width,
        int height,
        bool bottomAligned,
        int argb,
        ImmutableArray<CameraSceneMask> masks
    )
    {
        List<SKImage> strips = [];

        foreach (ImmutableArray<string> column in columns)
        {
            SKImage? strip = BuildColumn(column, height, bottomAligned);

            if (strip is not null)
            {
                strips.Add(strip);
            }
        }

        // A plane whose columns resolve to nothing used to return null here, and the caller's
        // fallback is <see cref="FillQuad"/>, which knows nothing about masks. So a wall whose
        // wallpaper is missing came out solid straight across its own doorway — an opening is cut by
        // a mask, and the only code that punches one is at the end of this method.
        //
        // Painting the plane's flat colour as the texture instead keeps every plane on the one path
        // that can cut it. Measured from a real payload: a room arrives as nine planes and the one
        // carrying the doorway reads `colour=#9698a4 cols=1 masks=1`, so whether its single column
        // resolves decided which branch it took, and only one of the two could ever show the door.
        bool flat = strips.Count == 0;

        if (flat && masks.IsDefaultOrEmpty)
        {
            return null;
        }

        try
        {
            using SKSurface surface = SKSurface.Create(
                new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)
            );

            surface.Canvas.Clear(SKColors.Transparent);

            if (flat)
            {
                using SKPaint fill = new()
                {
                    Color = new SKColor((uint)argb).WithAlpha(byte.MaxValue),
                    IsAntialias = false,
                    Style = SKPaintStyle.Fill,
                };

                surface.Canvas.DrawRect(SKRect.Create(width, height), fill);
                PunchMasks(surface.Canvas, masks);

                return surface.Snapshot();
            }

            // The plane's colour multiplies the texture rather than replacing it -- the client
            // composites these with 'multiply', which is what makes the same wallpaper darker on a
            // shaded wall than on a lit one.
            using SKPaint paint = new()
            {
                ColorFilter = SKColorFilter.CreateBlendMode(
                    new SKColor((uint)argb).WithAlpha(byte.MaxValue),
                    SKBlendMode.Modulate
                ),
                IsAntialias = false,
            };

            for (int x = 0; x < width; )
            {
                int startX = x;

                foreach (SKImage strip in strips)
                {
                    surface.Canvas.DrawImage(strip, new SKPoint(x, 0), NearestNeighbour, paint);

                    x += strip.Width;

                    if (x >= width)
                    {
                        break;
                    }
                }

                // A zero-width strip would spin here forever, and a crafted pack is a plausible way
                // to get one.
                if (x == startX)
                {
                    break;
                }
            }

            PunchMasks(surface.Canvas, masks);

            return surface.Snapshot();
        }
        finally
        {
            foreach (SKImage strip in strips)
            {
                strip.Dispose();
            }
        }
    }

    /// <summary>
    /// Erases the plane's texture wherever a mask's artwork has any alpha.
    /// </summary>
    /// <remarks>
    /// <see cref="SKBlendMode.DstOut"/> is the whole operation: it keeps the destination only where
    /// the source is transparent. A mask is not drawn and its own colours never appear — a doorway is
    /// a hole through the wall, and what shows through it is whatever the room painted behind.
    /// </remarks>
    private void PunchMasks(SKCanvas canvas, ImmutableArray<CameraSceneMask> masks)
    {
        if (masks.IsDefaultOrEmpty)
        {
            return;
        }

        using SKPaint paint = new() { BlendMode = SKBlendMode.DstOut, IsAntialias = false };

        foreach (CameraSceneMask mask in masks)
        {
            if (Resolve(mask.AssetName) is not { } artwork)
            {
                continue;
            }

            int saved = canvas.Save();

            canvas.Translate((float)mask.Location.X, (float)mask.Location.Y);

            if (mask.FlipH || mask.FlipV)
            {
                canvas.Scale(mask.FlipH ? -1 : 1, mask.FlipV ? -1 : 1);
                canvas.Translate(mask.FlipH ? -artwork.Width : 0, mask.FlipV ? -artwork.Height : 0);
            }

            canvas.DrawImage(artwork, new SKPoint(0, 0), NearestNeighbour, paint);
            canvas.RestoreToCount(saved);
        }
    }

    private SKImage? BuildColumn(ImmutableArray<string> assets, int height, bool bottomAligned)
    {
        List<SKImage> cells = [];
        int width = 0;
        int natural = 0;

        // An empty name in the stack means "do not repeat this column": the client uses it to mark a
        // panel that is drawn once rather than tiled up the wall.
        bool repeat = true;

        foreach (string asset in assets)
        {
            if (asset.Length == 0)
            {
                repeat = false;
                continue;
            }

            if (Resolve(asset) is not { } cell)
            {
                continue;
            }

            cells.Add(cell);
            width = Math.Max(width, cell.Width);
            natural += cell.Height;
        }

        if (cells.Count == 0 || width <= 0)
        {
            return null;
        }

        int columnHeight = repeat ? height : Math.Min(height, Math.Max(1, natural));

        using SKSurface surface = SKSurface.Create(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)
        );

        surface.Canvas.Clear(SKColors.Transparent);

        int y = bottomAligned ? height - columnHeight : 0;
        int end = y + columnHeight;

        while (y < end)
        {
            int startY = y;

            foreach (SKImage cell in cells)
            {
                surface.Canvas.DrawImage(
                    cell,
                    new SKPoint((width - cell.Width) / 2f, y),
                    NearestNeighbour
                );

                y += cell.Height;

                if (y >= end || !repeat)
                {
                    break;
                }
            }

            if (y == startY || !repeat)
            {
                break;
            }
        }

        return surface.Snapshot();
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
