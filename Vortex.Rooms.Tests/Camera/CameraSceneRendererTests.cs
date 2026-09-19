using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Camera;
using Vortex.Rooms.Camera.Render;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// What a photo contains and in what order, asserted without a raster library and without pixels.
/// </summary>
/// <remarks>
/// The canvas is recorded rather than drawn on, which is the whole reason it is an interface. Draw
/// order, the derived viewport and the transforms handed down are decisions, and a decision that can
/// only be checked by comparing two images is a decision nobody checks.
/// </remarks>
public sealed class CameraSceneRendererTests
{
    private const int MaxDimension = 1600;

    [Fact]
    public void ThePlanesComeFirst_ThenTheSprites_InTheOrderTheSceneListsThem()
    {
        Recording canvas = Render(
            SceneWith(
                planes: [Plane(10, 10, 0xFF0000), Plane(20, 20, 0x00FF00)],
                sprites: [Sprite("floor_a"), Sprite("chair"), Sprite("avatar_head")]
            )
        );

        // The client composited in this order before writing the payload. Honouring it is more
        // faithful than any depth rule this side could invent.
        canvas
            .Calls.Should()
            .Equal(
                "clear",
                "quad:16711680",
                "quad:65280",
                "sprite:floor_a",
                "sprite:chair",
                "sprite:avatar_head"
            );
    }

    /// <summary>
    /// The payload never says how big the photo is, and the client never has to: it unshifts a
    /// background plane spanning its whole viewport, so the <b>first</b> plane is the frame.
    /// </summary>
    /// <remarks>
    /// This test had that fact in its own comment and drew the other conclusion from it — that the
    /// planes' extent is the viewport. It is not, and not by a little: <c>getRoomPlanes</c> culls
    /// only a plane whose four corners are off the same side, so a wall rising above the top edge and
    /// a floor running past the right one are ordinary. The canvas grew to the furthest corner of the
    /// room, the background painted its own rectangle in the corner of it, and a real photo came back
    /// with a hard straight edge where the room stopped being painted.
    /// </remarks>
    [Fact]
    public void TheViewportIsTheBackgroundPlane_NotTheExtentOfTheRoom()
    {
        CameraViewport viewport = new CameraSceneRenderer(MaxDimension).ViewportFor(
            SceneWith(planes: [Plane(640, 480, 0), Plane(100, 900, 0)], sprites: [])
        );

        viewport.Should().Be(new CameraViewport(640, 480));
    }

    /// <summary>
    /// "Plane zero is the frame" is true of the client that exists, and the payload is written by
    /// whoever is on the other end of the socket. A first plane that is not anchored at the origin is
    /// not a frame, so the scene falls back to its own extent rather than to a one-pixel canvas.
    /// </summary>
    [Fact]
    public void AFirstPlaneThatIsNotAFrame_FallsBackToTheExtentOfThePlanes()
    {
        CameraScenePlane offset = Plane(300, 200, 0) with
        {
            CornerPoints =
            [
                new CameraScenePoint(300, 200),
                new CameraScenePoint(-40, 200),
                new CameraScenePoint(300, 20),
                new CameraScenePoint(-40, 20),
            ],
        };

        CameraViewport viewport = new CameraSceneRenderer(MaxDimension).ViewportFor(
            SceneWith(planes: [offset, Plane(100, 900, 0)], sprites: [])
        );

        viewport.Should().Be(new CameraViewport(300, 900));
    }

    /// <summary>
    /// Nothing upstream bounds a coordinate: a corner at a billion is a valid number in a valid
    /// document, and allocating the surface it asks for is how a photo becomes an out-of-memory.
    /// </summary>
    [Fact]
    public void AViewportBiggerThanTheHotelAllows_IsClampedRatherThanAllocated()
    {
        CameraViewport viewport = new CameraSceneRenderer(MaxDimension).ViewportFor(
            SceneWith(planes: [Plane(1_000_000_000, 5, 0)], sprites: [])
        );

        viewport.Should().Be(new CameraViewport(MaxDimension, 5));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AViewportThatIsNotANumber_StillProducesADrawableSurface(double corner)
    {
        // The decoder drops a non-finite coordinate to zero, but the renderer is not entitled to
        // assume that: one pixel is a photo nobody wants, which beats a surface nobody can allocate.
        CameraViewport viewport = new CameraSceneRenderer(MaxDimension).ViewportFor(
            SceneWith(planes: [Plane(corner, corner, 0)], sprites: [])
        );

        viewport.Width.Should().Be(1);
        viewport.Height.Should().Be(1);
    }

    [Fact]
    public void ASceneWithNoPlanesAtAll_StillRenders()
    {
        Recording canvas = Render(SceneWith(planes: [], sprites: [Sprite("lone_sprite")]));

        canvas.Calls.Should().Equal("clear", "sprite:lone_sprite");
        canvas.Viewport.Should().Be(new CameraViewport(1, 1));
    }

    [Fact]
    public void ASpritesLookIsCarriedDownUnchanged()
    {
        Recording canvas = Render(
            SceneWith(
                planes: [],
                sprites:
                [
                    Sprite("badge") with
                    {
                        X = 12,
                        Y = 34,
                        Alpha = 128,
                        FlipH = true,
                        Skew = 0.5,
                        Color = 0xABCDEF,
                        BlendMode = "add",
                    },
                ]
            )
        );

        CameraSpriteDraw draw = canvas.Draws.Should().ContainSingle().Subject;

        draw.Should()
            .Be(
                new CameraSpriteDraw
                {
                    X = 12,
                    Y = 34,
                    Alpha = 128,
                    FlipH = true,
                    Skew = 0.5,
                    Color = 0xABCDEF,
                    BlendMode = "add",
                }
            );
    }

    [Fact]
    public void TheSurfaceStartsTransparent()
    {
        // A room photo is not rectangular at its corners. Clearing to opaque black would frame every
        // shot in a box the client never drew.
        Render(SceneWith(planes: [], sprites: [])).ClearedWith.Should().Be(0x00000000);
    }

    /// <summary>
    /// The plane the canvas is asked to fill encloses the area the plane actually covers.
    /// </summary>
    /// <remarks>
    /// Stated as an area rather than as the expected corner order, because the area is the thing that
    /// was wrong and the order is only how. The client sorts its corners bottom-right, bottom-left,
    /// top-right, top-left; walked in that order the path crosses itself, and a symmetric bowtie
    /// encloses its two halves with opposite winding — so the shoelace area comes out at exactly
    /// zero, whatever the plane's size. Every plane in the first real photo was drawn that way.
    /// </remarks>
    [Fact]
    public void APlaneEnclosesItsOwnArea()
    {
        Recording recording = Render(SceneWith(planes: [Plane(100, 50, 0xFF0000)], sprites: []));

        ImmutableArray<CameraScenePoint> quad = recording.Quads.Should().ContainSingle().Subject;

        ShoelaceArea(quad).Should().Be(100 * 50);
    }

    private static double ShoelaceArea(ImmutableArray<CameraScenePoint> polygon)
    {
        double twice = 0;

        for (int i = 0; i < polygon.Length; i++)
        {
            CameraScenePoint current = polygon[i];
            CameraScenePoint next = polygon[(i + 1) % polygon.Length];

            twice += (current.X * next.Y) - (next.X * current.Y);
        }

        return Math.Abs(twice) / 2;
    }

    /// <summary>What the canvas was asked to do, once the renderer has finished with it.</summary>
    private sealed record Recording(
        List<string> Calls,
        List<CameraSpriteDraw> Draws,
        int? ClearedWith,
        CameraViewport Viewport,
        List<ImmutableArray<CameraScenePoint>> Quads
    );

    private static Recording Render(CameraScene scene)
    {
        // Disposed here as well as by the renderer. Its Dispose does nothing, and saying the scope
        // owns it is cheaper than explaining to the analyser that the lambda hands it away.
        using RecordingCanvas canvas = new();
        CameraViewport viewport = default;

        // The renderer owns the canvas and disposes it; only what it wrote down leaves this method.
        new CameraSceneRenderer(MaxDimension).Render(
            scene,
            actual =>
            {
                viewport = actual;
                return canvas;
            }
        );

        return new Recording(
            canvas.Calls,
            canvas.Draws,
            canvas.ClearedWith,
            viewport,
            canvas.Quads
        );
    }

    /// <summary>
    /// A plane with its corners in the order the client actually sends them: bottom-right,
    /// bottom-left, top-right, top-left, as <c>SpriteDataCollector.sortQuadPoints</c> leaves them.
    /// That is a sort order, not a path — feeding these tests a tidy perimeter instead was how the
    /// renderer passed every one of them while drawing bowties.
    /// </summary>
    private static CameraScenePlane Plane(double right, double bottom, int color) =>
        new()
        {
            Z = 0,
            CornerPoints =
            [
                new CameraScenePoint(right, bottom),
                new CameraScenePoint(0, bottom),
                new CameraScenePoint(right, 0),
                new CameraScenePoint(0, 0),
            ],
            Color = color,
            Masks = [],
            TextureColumns = [],
            BottomAligned = false,
        };

    private static CameraSceneSprite Sprite(string name) =>
        new()
        {
            Name = name,
            Source = CameraSpriteSource.LocalAsset,
            X = 0,
            Y = 0,
            Z = 0,
            Alpha = 255,
            FlipH = false,
            Skew = 0,
            Frame = 0,
            Color = null,
            BlendMode = null,
            Width = 0,
            Height = 0,
            PaletteSourceName = null,
        };

    private static CameraScene SceneWith(
        ImmutableArray<CameraScenePlane> planes,
        ImmutableArray<CameraSceneSprite> sprites
    ) =>
        new()
        {
            RoomId = 1,
            Zoom = 1,
            Planes = planes,
            Sprites = sprites,
            EffectsJson = "[]",
            TakenAtUtc = DateTime.UnixEpoch,
            RejectedExternalSprites = 0,
        };

    /// <summary>A canvas that writes down what it was asked to do and draws nothing.</summary>
    private sealed class RecordingCanvas : ICameraCanvas
    {
        public List<string> Calls { get; } = [];

        public List<CameraSpriteDraw> Draws { get; } = [];

        public int? ClearedWith { get; private set; }

        public void Clear(int argb)
        {
            ClearedWith = argb;
            Calls.Add("clear");
        }

        public List<ImmutableArray<CameraScenePoint>> Quads { get; } = [];

        public void FillQuad(ImmutableArray<CameraScenePoint> corners, int argb)
        {
            corners
                .Should()
                .HaveCount(4, "a plane is a quad and the decoder refuses anything else");
            Quads.Add(corners);
            Calls.Add($"quad:{argb}");
        }

        public bool DrawSprite(string name, CameraSpriteDraw draw)
        {
            Calls.Add($"sprite:{name}");
            Draws.Add(draw);
            return true;
        }

        public byte[] EncodePng() => [];

        public void Dispose() { }
    }
}
