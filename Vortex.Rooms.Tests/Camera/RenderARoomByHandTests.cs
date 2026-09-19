using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Vortex.Primitives.Camera;
using Vortex.Rooms.Camera.Render;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// Renders an isometric room the way the client would describe one and writes the PNG out, so it can
/// be looked at. Not an assertion — a darkroom.
/// </summary>
/// <remarks>
/// <para>
/// Skipped on purpose: it asserts nothing worth failing over, and the assertions about plane geometry
/// live in <see cref="CameraSceneRendererTests"/> where they belong. This exists because a rasteriser
/// is the one thing a green suite cannot vouch for — the first real photo came back as grey shards,
/// and no test in the repository had an opinion about it.
/// </para>
/// <para>
/// The corners go through a faithful port of the client's own <c>sortQuadPoints</c> rather than a
/// tidy perimeter, which is the entire point: it was a plausible-looking corner order that produced
/// the shards, so a fixture that quietly fixes the order proves nothing. Reach for this before
/// deploying anything that changes what a photo contains — masks, texture columns, sprites — because
/// the alternative is a nine-minute push and a photo that can only be rendered once.
/// </para>
/// </remarks>
public sealed class RenderARoomByHandTests
{
    private const int TileWidth = 64;
    private const int TileHeight = 32;
    private const int OffsetX = 340;
    private const int OffsetY = 60;

    [Fact(Skip = "A darkroom, not a check: un-skip it, run it, open the path it prints.")]
    public void WriteARoom()
    {
        List<CameraScenePlane> planes = [];

        // Floor, 8x8. Each tile is a plane whose quad is origin, origin+left, origin+right,
        // origin+left+right -- exactly how SpriteDataCollector builds one before sorting it.
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                planes.Add(
                    Tile(
                        Floor(x, y),
                        Floor(x, y + 1),
                        Floor(x + 1, y),
                        Floor(x + 1, y + 1),
                        0x989865
                    )
                );
            }
        }

        // Two walls, as vertical quads rising from the floor edge.
        for (int y = 0; y < 8; y++)
        {
            CameraScenePoint bottomNear = Floor(0, y);
            CameraScenePoint bottomFar = Floor(0, y + 1);

            planes.Add(
                Tile(bottomNear, bottomFar, Raise(bottomNear, 110), Raise(bottomFar, 110), 0x8E8E8E)
            );
        }

        for (int x = 0; x < 8; x++)
        {
            CameraScenePoint bottomNear = Floor(x, 0);
            CameraScenePoint bottomFar = Floor(x + 1, 0);

            planes.Add(
                Tile(bottomNear, bottomFar, Raise(bottomNear, 110), Raise(bottomFar, 110), 0xB6B6B6)
            );
        }

        CameraScene scene = new()
        {
            RoomId = 1,
            Zoom = 1,
            Planes = [.. planes],
            Sprites = [],
            EffectsJson = "[]",
            TakenAtUtc = DateTime.UnixEpoch,
            RejectedExternalSprites = 0,
        };

        byte[] png = new CameraSceneRenderer(1600).Render(
            scene,
            viewport => new SkiaCameraCanvas(viewport, _ => null)
        );

        string path = Path.Combine(Path.GetTempPath(), "vortex-room-render.png");

        File.WriteAllBytes(path, png);

        Assert.True(png.Length > 0, path);
    }

    private static CameraScenePoint Floor(int x, int y) =>
        new(OffsetX + ((x - y) * (TileWidth / 2.0)), OffsetY + ((x + y) * (TileHeight / 2.0)));

    private static CameraScenePoint Raise(CameraScenePoint point, double height) =>
        new(point.X, point.Y - height);

    /// <summary>
    /// The four corners put through the client's own <c>sortQuadPoints</c>, so what the renderer
    /// receives here is byte-for-byte the arrangement a real payload carries.
    /// </summary>
    private static CameraScenePlane Tile(
        CameraScenePoint origin,
        CameraScenePoint left,
        CameraScenePoint right,
        CameraScenePoint far,
        int color
    ) =>
        new()
        {
            Z = 0,
            CornerPoints = SortQuadPoints(origin, left, right, far),
            Color = color,
            Masks = [],
            TextureColumns = [],
            BottomAligned = false,
        };

    /// <summary>
    /// A faithful port of <c>SpriteDataCollector.sortQuadPoints</c>. Reimplemented rather than
    /// approximated: the whole question is what order the corners arrive in.
    /// </summary>
    private static ImmutableArray<CameraScenePoint> SortQuadPoints(
        CameraScenePoint a,
        CameraScenePoint b,
        CameraScenePoint c,
        CameraScenePoint d
    )
    {
        CameraScenePoint[] points;

        if (a.X == b.X)
        {
            points = [a, c, b, d];
        }
        else if (a.X == c.X)
        {
            points = [a, b, c, d];
        }
        else if ((b.X < a.X && b.Y > a.Y) || (b.X > a.X && b.Y < a.Y))
        {
            points = [a, c, b, d];
        }
        else
        {
            points = [a, b, c, d];
        }

        if (points[0].X < points[1].X)
        {
            points = [points[1], points[0], points[3], points[2]];
        }

        if (points[0].Y < points[2].Y)
        {
            points = [points[2], points[3], points[0], points[1]];
        }

        return [.. points];
    }
}
