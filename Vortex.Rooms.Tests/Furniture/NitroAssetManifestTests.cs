using System.Text;
using FluentAssertions;
using Vortex.Furniture.Assets;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Turning an asset name into a rectangle on the sheet.
/// </summary>
/// <remarks>
/// The camera payload names a sprite and nothing else — no size, no bundle, no frame. Every pixel a
/// photo shows of a furniture is found through this lookup, so the indirections are the feature, not
/// an edge: most furniture ships four directions and mirrors the other four rather than drawing
/// them.
/// </remarks>
public sealed class NitroAssetManifestTests
{
    [Fact]
    public void AnAssetWithItsOwnFrame_ResolvesToIt()
    {
        NitroAssetManifest manifest = Read(
            """
            {
              "spritesheet": {
                "frames": {
                  "chair_64_a_0_0": { "frame": { "x": 10, "y": 20, "w": 30, "h": 40 } }
                }
              }
            }
            """
        );

        manifest.TryResolve("chair_64_a_0_0", out NitroSpriteFrame frame).Should().BeTrue();

        frame.Should().Be(new NitroSpriteFrame(10, 20, 30, 40, FlipH: false, FlipV: false));
    }

    /// <summary>The mirrored direction: a name with no frame of its own, standing on another's.</summary>
    [Theory]
    [InlineData("aliases", "link")]
    [InlineData("assets", "source")]
    public void AMirroredDirection_BorrowsTheFrameItPointsAt(string section, string key)
    {
        NitroAssetManifest manifest = Read(
            $$"""
            {
              "{{section}}": { "chair_64_a_4_0": { "{{key}}": "chair_64_a_2_0", "flipH": true } },
              "spritesheet": {
                "frames": { "chair_64_a_2_0": { "frame": { "x": 1, "y": 2, "w": 3, "h": 4 } } }
              }
            }
            """
        );

        manifest.TryResolve("chair_64_a_4_0", out NitroSpriteFrame frame).Should().BeTrue();

        frame.Should().Be(new NitroSpriteFrame(1, 2, 3, 4, FlipH: true, FlipV: false));
    }

    /// <summary>
    /// Flips accumulate by exclusive-or, so a mirror of a mirror is the original. Getting this wrong
    /// draws a chair facing the wrong way, which no test of the crop rectangle would notice.
    /// </summary>
    [Fact]
    public void AMirrorOfAMirror_IsTheOriginalAgain()
    {
        NitroAssetManifest manifest = Read(
            """
            {
              "aliases": {
                "c": { "link": "b", "flipH": true },
                "b": { "link": "a", "flipH": true }
              },
              "spritesheet": { "frames": { "a": { "frame": { "x": 0, "y": 0, "w": 8, "h": 8 } } } }
            }
            """
        );

        manifest.TryResolve("c", out NitroSpriteFrame frame).Should().BeTrue();

        frame.FlipH.Should().BeFalse();
    }

    [Fact]
    public void ANameNothingDeclares_DoesNotResolve()
    {
        NitroAssetManifest manifest = Read(
            """{ "spritesheet": { "frames": { "a": { "frame": { "x": 0, "y": 0, "w": 1, "h": 1 } } } } }"""
        );

        manifest.TryResolve("never_packed", out _).Should().BeFalse();
    }

    /// <summary>A bundle can point a name at itself, or round a ring. Neither may spin.</summary>
    [Theory]
    [InlineData("""{ "aliases": { "a": { "link": "a" } } }""")]
    [InlineData("""{ "aliases": { "a": { "link": "b" }, "b": { "link": "a" } } }""")]
    public void AnAliasThatNeverLands_Stops(string json)
    {
        Read(json).TryResolve("a", out _).Should().BeFalse();
    }

    /// <summary>An empty animation step. Cropping to it is an image that cannot be allocated.</summary>
    [Fact]
    public void AFrameWithNoArea_IsNotAFrame()
    {
        NitroAssetManifest manifest = Read(
            """{ "spritesheet": { "frames": { "a": { "frame": { "x": 0, "y": 0, "w": 0, "h": 12 } } } } }"""
        );

        manifest.FrameCount.Should().Be(0);
        manifest.TryResolve("a", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public void AManifestThatIsNotOne_IsRefused(string json)
    {
        NitroAssetManifest.TryRead(Encoding.UTF8.GetBytes(json), out _).Should().BeFalse();
    }

    private static NitroAssetManifest Read(string json)
    {
        NitroAssetManifest
            .TryRead(Encoding.UTF8.GetBytes(json), out NitroAssetManifest? read)
            .Should()
            .BeTrue();

        return read!;
    }
}
