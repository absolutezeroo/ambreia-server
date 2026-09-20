using System.Collections.Immutable;
using FluentAssertions;
using SkiaSharp;
using Vortex.Primitives.Camera;
using Vortex.Rooms.Camera.Render;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// The one part of the camera a recording canvas cannot speak for: that pixels actually come out.
/// </summary>
/// <remarks>
/// Draw order and transforms are asserted elsewhere without a raster library. What is left here is
/// whether the library is wired up at all — that the native asset resolves on this machine, that a
/// fill lands where the scene said, that a sprite's alpha and tint reach it, and that the result
/// encodes as a PNG. A green build proves none of that: SkiaSharp compiles happily and dies at the
/// first draw when its native half is missing, which is exactly what a container without it does.
/// </remarks>
public sealed class SkiaCameraCanvasTests
{
    private static readonly ImmutableArray<CameraScenePoint> WholeSurface =
    [
        new(0, 0),
        new(4, 0),
        new(4, 4),
        new(0, 4),
    ];

    [Fact]
    public void AFilledQuad_LandsOnTheSurfaceOpaque()
    {
        using SkiaCameraCanvas canvas = new(new CameraViewport(4, 4), _ => null);

        canvas.Clear(0x00000000);
        canvas.FillQuad(WholeSurface, 0x00FF0000);

        SKColor pixel = PixelAt(canvas, 2, 2);

        // Opaque even though the plane colour carried no alpha: a room surface is not a decal, and
        // inheriting whatever the top byte happened to be would make a floor invisible.
        pixel.Red.Should().Be(255);
        pixel.Green.Should().Be(0);
        pixel.Blue.Should().Be(0);
        pixel.Alpha.Should().Be(255);
    }

    [Fact]
    public void AClearedSurface_IsTransparent()
    {
        using SkiaCameraCanvas canvas = new(new CameraViewport(2, 2), _ => null);

        canvas.Clear(0x00000000);

        PixelAt(canvas, 1, 1).Alpha.Should().Be(0);
    }

    [Fact]
    public void ASpriteThatResolvesToNothing_IsReportedRatherThanThrown()
    {
        using SkiaCameraCanvas canvas = new(new CameraViewport(4, 4), _ => null);

        // A pack that is one frame short should still take pictures; the hole is the photo's, not
        // the render's.
        canvas.DrawSprite("missing", Draw()).Should().BeFalse();
    }

    [Fact]
    public void ASpriteIsDrawnWhereTheSceneSaid()
    {
        byte[] red = OnePixel(SKColors.Red);

        using SkiaCameraCanvas canvas = new(new CameraViewport(4, 4), _ => red);

        canvas.Clear(0x00000000);
        canvas.DrawSprite("dot", Draw() with { X = 2, Y = 3 }).Should().BeTrue();

        PixelAt(canvas, 2, 3).Red.Should().Be(255);
        PixelAt(canvas, 0, 0).Alpha.Should().Be(0);
    }

    [Fact]
    public void ASpritesAlphaReachesTheSurface()
    {
        byte[] white = OnePixel(SKColors.White);

        using SkiaCameraCanvas canvas = new(new CameraViewport(2, 2), _ => white);

        canvas.Clear(0x00000000);
        canvas.DrawSprite("dot", Draw() with { Alpha = 128 });

        PixelAt(canvas, 0, 0).Alpha.Should().BeInRange(120, 136);
    }

    [Fact]
    public void ASpritesTintMultipliesIt()
    {
        byte[] white = OnePixel(SKColors.White);

        using SkiaCameraCanvas canvas = new(new CameraViewport(2, 2), _ => white);

        canvas.Clear(0x00000000);
        canvas.DrawSprite("dot", Draw() with { Color = 0x0000FF00 });

        SKColor pixel = PixelAt(canvas, 0, 0);

        pixel.Green.Should().Be(255);
        pixel.Red.Should().Be(0);
        pixel.Blue.Should().Be(0);
    }

    [Fact]
    public void AWallWhoseWallpaperIsMissing_StillShowsItsDoorway()
    {
        byte[] white = OnePixel(SKColors.White);

        // Only the mask resolves. That is the case a real photo hit: the plane carrying the doorway
        // arrives as `cols=1 masks=1`, and when its one column answered nothing the plane fell
        // through to the flat-colour fallback, which punches no mask — a solid wall straight across
        // the opening.
        using SkiaCameraCanvas canvas = new(
            new CameraViewport(4, 4),
            name => name == "door_64" ? white : null
        );

        canvas.Clear(0x00000000);

        canvas
            .FillTexturedQuad(
                [new(0, 0), new(4, 0), new(0, 4), new(4, 4)],
                0x00FF0000,
                [
                    ["wall_texture_missing"],
                ],
                false,
                [
                    new CameraSceneMask
                    {
                        AssetName = "door_64",
                        Location = new CameraScenePoint(0, 0),
                        FlipH = false,
                        FlipV = false,
                    },
                ]
            )
            .Should()
            .BeTrue();

        // Where the hole lands is the next test's business; this one is only that there is one, and
        // that the rest of the plane is still painted.
        int punched = 0;

        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                if (PixelAt(canvas, x, y).Alpha == 0)
                {
                    punched++;
                }
            }
        }

        punched.Should().BeInRange(1, 15);
        PixelAt(canvas, 2, 2).Red.Should().Be(255);
    }

    [Fact]
    public void AMaskIsReadInTheTextureSpaceTheClientMeasuredItIn()
    {
        // The wall that carries the doorway in a real photo, shifted 140px right so it fits a test
        // surface: thirteen tiles, `u = (416,-208)` from its top-left corner, `v = (0,115)`, which
        // the client rasterises 416 x 115. Its mask sits at x=256 of that, and the room's own door
        // tile — a plane of its own in the same payload — is centred 256px along the wall from that
        // corner. Both of those are measurements, not derivations; this test is the arithmetic that
        // has to agree with them.
        byte[] white = Block(64, 115, SKColors.White);

        using SkiaCameraCanvas canvas = new(new CameraViewport(430, 470), _ => white);

        canvas.Clear(0x00000000);

        canvas
            .FillTexturedQuad(
                [new(421, 251), new(4, 459), new(421, 136), new(4, 344)],
                0x00FF0000,
                [
                    ["wall_texture_64_1"],
                ],
                false,
                [
                    new CameraSceneMask
                    {
                        AssetName = "door",
                        Location = new CameraScenePoint(256, 29),
                        FlipH = false,
                        FlipV = false,
                    },
                ]
            )
            .Should()
            .BeTrue();

        // The middle of the punched band. The hole's own left edge is at 4 + 256 = 260, which is the
        // door tile; taking `corners[0]` as the origin instead puts it at 165, three tiles away.
        PixelAt(canvas, 292, 286).Alpha.Should().Be(0);

        // And the wall is still there where the wrong basis would have cut it.
        PixelAt(canvas, 165, 340).Alpha.Should().Be(255);
    }

    [Fact]
    public void WhatWasDrawnEncodesAsAPng()
    {
        using SkiaCameraCanvas canvas = new(new CameraViewport(4, 4), _ => null);

        canvas.Clear(0x00000000);
        canvas.FillQuad(WholeSurface, 0x000000FF);

        byte[] png = canvas.EncodePng();

        // The eight-byte PNG signature. The client asks for image/png and checks nothing else.
        png.Should().StartWith([(byte)0x89, (byte)'P', (byte)'N', (byte)'G']);
    }

    private static CameraSpriteDraw Draw() =>
        new()
        {
            X = 0,
            Y = 0,
            Alpha = 255,
            FlipH = false,
            Skew = 0,
            Color = null,
            BlendMode = null,
        };

    private static byte[] Block(int width, int height, SKColor color)
    {
        using SKBitmap bitmap = new(width, height);

        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(color);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    private static byte[] OnePixel(SKColor color)
    {
        using SKBitmap bitmap = new(1, 1);
        bitmap.SetPixel(0, 0, color);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    private static SKColor PixelAt(SkiaCameraCanvas canvas, int x, int y)
    {
        using SKBitmap decoded = SKBitmap.Decode(canvas.EncodePng());

        return decoded.GetPixel(x, y);
    }
}
