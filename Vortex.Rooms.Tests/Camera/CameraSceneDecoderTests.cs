using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Vortex.Primitives.Camera;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// The camera's intake. <c>RenderRoom</c> carries one zlib blob the client built, and the client's
/// own <c>isSendable()</c> returns <c>true</c> without looking at it — every ceiling is this side's.
/// </summary>
/// <remarks>
/// These are the cases a live hotel never produces and an attacker produces first: a payload that
/// inflates to gigabytes, a sprite naming the loopback interface, a plane with three corners, a scene
/// shot in somebody else's room, a scene from last week.
/// </remarks>
public sealed class CameraSceneDecoderTests
{
    private const int ROOM = 4242;

    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AWellFormedScene_DecodesToItsPlanesAndSprites()
    {
        CameraSceneDecodeResult result = Decode(Scene());

        result.Accepted.Should().BeTrue();
        result.Refusal.Should().Be(CameraRenderRefusal.None);

        CameraScene scene = result.Scene!;
        scene.RoomId.Should().Be(ROOM);
        scene.Zoom.Should().Be(1);
        scene.Planes.Should().ContainSingle();
        scene.Planes[0].CornerPoints.Should().HaveCount(4);
        scene.Planes[0].Masks.Should().ContainSingle().Which.AssetName.Should().Be("window_mask");
        scene.Sprites.Should().ContainSingle();
        scene.Sprites[0].Name.Should().Be("chair_a_0");
        scene.Sprites[0].Source.Should().Be(CameraSpriteSource.LocalAsset);
        scene.RejectedExternalSprites.Should().Be(0);
    }

    [Fact]
    public void AnEmptyPayload_IsRefusedRatherThanThrown()
    {
        CameraSceneDecoder
            .Decode([], ROOM, Now, CameraSceneLimits.Defaults)
            .Refusal.Should()
            .Be(CameraRenderRefusal.Empty);
    }

    [Fact]
    public void SomethingThatIsNotZlib_IsRefusedRatherThanThrown()
    {
        // A malformed payload is the expected case on a hostile wire, not an exceptional one.
        byte[] rubbish = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

        CameraSceneDecoder
            .Decode(rubbish, ROOM, Now, CameraSceneLimits.Defaults)
            .Refusal.Should()
            .Be(CameraRenderRefusal.CorruptPayload);
    }

    [Fact]
    public void ACompressedPayloadOverTheCeiling_IsRefusedBeforeItIsInflated()
    {
        byte[] payload = Deflate(Scene());
        CameraSceneLimits limits = CameraSceneLimits.Defaults with
        {
            MaxPayloadBytes = payload.Length - 1,
        };

        CameraSceneDecoder
            .Decode(payload, ROOM, Now, limits)
            .Refusal.Should()
            .Be(CameraRenderRefusal.PayloadTooLarge);
    }

    /// <summary>
    /// The compression bomb. Ten megabytes of one repeated byte deflate to a few hundred, so the
    /// blob passes any check on its wire size and only the ceiling applied <em>while</em> inflating
    /// stops it.
    /// </summary>
    [Fact]
    public void APayloadThatInflatesPastTheCeiling_IsRefusedWhileInflating()
    {
        byte[] bomb = Deflate(new string('a', 10 * 1024 * 1024));

        bomb.Length.Should()
            .BeLessThan(64 * 1024, "the whole point is that it is small on the wire");

        CameraSceneLimits limits = CameraSceneLimits.Defaults with { MaxSceneBytes = 64 * 1024 };

        CameraSceneDecoder
            .Decode(bomb, ROOM, Now, limits)
            .Refusal.Should()
            .Be(CameraRenderRefusal.SceneTooLarge);
    }

    [Fact]
    public void InflatedRubbishThatIsNotTheClientsDocument_IsRefused()
    {
        Decode("not json at all").Refusal.Should().Be(CameraRenderRefusal.MalformedScene);
        Decode("[]").Refusal.Should().Be(CameraRenderRefusal.MalformedScene);
    }

    [Fact]
    public void ADocumentNestedPastAnyRealScene_IsRefusedRatherThanRecursedInto()
    {
        string bracketBomb = new string('[', 5000) + new string(']', 5000);

        Decode(bracketBomb).Refusal.Should().Be(CameraRenderRefusal.MalformedScene);
    }

    [Fact]
    public void ASceneNamingAnotherRoom_IsRefused()
    {
        // The session says where the player is standing. A photo of somewhere else is not theirs
        // to take, whatever their client claims.
        Decode(Scene(roomId: ROOM + 1)).Refusal.Should().Be(CameraRenderRefusal.WrongRoom);
    }

    [Theory]
    [InlineData(-3600)]
    [InlineData(3600)]
    public void ASceneOutsideTheFreshnessWindow_IsRefusedInBothDirections(int offsetSeconds)
    {
        // Behind is a replay. Ahead is a payload built by hand, because a real client's clock is
        // not an hour fast.
        long timestamp = new DateTimeOffset(Now.AddSeconds(offsetSeconds)).ToUnixTimeMilliseconds();

        Decode(Scene(timestamp: timestamp)).Refusal.Should().Be(CameraRenderRefusal.Stale);
    }

    [Fact]
    public void MorePlanesOrSpritesThanTheHotelAccepts_AreRefused()
    {
        CameraSceneLimits tight = CameraSceneLimits.Defaults with { MaxPlanes = 1, MaxSprites = 1 };

        Decode(Scene(planeCount: 2), tight).Refusal.Should().Be(CameraRenderRefusal.TooComplex);
        Decode(Scene(spriteCount: 2), tight).Refusal.Should().Be(CameraRenderRefusal.TooComplex);
    }

    [Fact]
    public void APlaneThatIsNotAQuad_IsRefused()
    {
        string threeCorners = Scene()
            .Replace(
                """{"x":0,"y":0},{"x":10,"y":0},{"x":10,"y":10},{"x":0,"y":10}""",
                """{"x":0,"y":0},{"x":10,"y":0},{"x":10,"y":10}""",
                StringComparison.Ordinal
            );

        Decode(threeCorners).Refusal.Should().Be(CameraRenderRefusal.MalformedScene);
    }

    /// <summary>
    /// The server-side request forgery. A sprite name beginning <c>http</c> is an address the
    /// renderer is told to fetch, and the payload chose it.
    /// </summary>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1:3306/")]
    [InlineData("http://assets.example.evil.test/c_images/x.png")]
    [InlineData("http://assets.example@evil.test/c_images/x.png")]
    [InlineData("https://assets.example/c_images/x.png")]
    [InlineData("http://assets.example:8080/c_images/x.png")]
    [InlineData("http://assets.example/private/x.png")]
    public void ASpriteNamingAnythingButAnAllowedOrigin_IsDropped(string url)
    {
        CameraSceneDecodeResult result = Decode(Scene(spriteName: url), WithAllowedHost());

        result.Accepted.Should().BeTrue("one bad name must not deny the camera to the whole room");
        result.Scene!.Sprites.Should().BeEmpty();
        result.Scene.RejectedExternalSprites.Should().Be(1);
    }

    [Fact]
    public void ASpriteOnTheAllowedOrigin_IsKeptAsAnExternalImage()
    {
        CameraSceneDecodeResult result = Decode(
            Scene(spriteName: "http://assets.example/c_images/badge.gif"),
            WithAllowedHost()
        );

        CameraSceneSprite sprite = result.Scene!.Sprites.Should().ContainSingle().Subject;
        sprite.Source.Should().Be(CameraSpriteSource.ExternalImage);
        result.Scene.RejectedExternalSprites.Should().Be(0);
    }

    [Fact]
    public void WithNothingAllowlisted_EvenAPlausibleUrlIsDropped()
    {
        // The default. External images are opted into, never on by accident.
        CameraSceneDecodeResult result = Decode(
            Scene(spriteName: "http://assets.example/c_images/badge.gif")
        );

        result.Scene!.Sprites.Should().BeEmpty();
        result.Scene.RejectedExternalSprites.Should().Be(1);
    }

    /// <summary>
    /// Nothing builds a path out of an asset key — the renderer looks it up in the loaded index — so
    /// this is the second lock. It exists because the day somebody writes the obvious
    /// <c>Path.Combine(root, name)</c> is the day the first one stops existing.
    /// </summary>
    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\windows\\win.ini")]
    [InlineData("/etc/shadow")]
    [InlineData("C:\\secrets.txt")]
    public void ASpriteNamingAPath_IsDropped(string name)
    {
        CameraSceneDecodeResult result = Decode(Scene(spriteName: name));

        result.Accepted.Should().BeTrue();
        result.Scene!.Sprites.Should().BeEmpty();
        // Not counted as a rejected external image: it never claimed to be a url.
        result.Scene.RejectedExternalSprites.Should().Be(0);
    }

    [Fact]
    public void AZoomTheWidgetCannotProduce_FallsBackToOneRatherThanLosingThePhoto()
    {
        Decode(Scene(zoom: 9999)).Scene!.Zoom.Should().Be(1);
        Decode(Scene(zoom: 2)).Scene!.Zoom.Should().Be(2);
    }

    /// <summary>
    /// The crash that reached production. JsonElement.TryGetInt32 THROWS when the element is not a
    /// Number -- it returns false only for a Number that will not fit -- and a sprite's colour
    /// arrives as a string: the served client does sprite.color.toString(), where the AS3 it was
    /// ported from writes int(param1.color). The exception left the grain through the handler, so
    /// the player got no reply at all rather than a refusal.
    /// </summary>
    [Fact]
    public void ASpriteColourWrittenAsAString_IsReadRatherThanThrownOn()
    {
        CameraSceneDecodeResult result = Decode(Scene(spriteColorJson: "\"16777215\""));

        result.Accepted.Should().BeTrue();
        result.Scene!.Sprites.Should().ContainSingle().Which.Color.Should().Be(16777215);
    }

    [Fact]
    public void ASpriteColourWrittenAsANumber_StillReads()
    {
        // The AS3's own shape. Both have to work: the two clients disagree and either may connect.
        CameraSceneDecodeResult result = Decode(Scene(spriteColorJson: "16777215"));

        result.Scene!.Sprites.Should().ContainSingle().Which.Color.Should().Be(16777215);
    }

    /// <summary>
    /// The third spelling, and the one that was silently dropped.
    /// </summary>
    /// <remarks>
    /// An avatar's body parts do not go through <c>fromSprite</c>. <c>AvatarImageCache</c>'s camera
    /// branch writes their colour itself, as <c>'0x' + hex + hex + hex</c>, so the decimal parse
    /// refused it and every avatar in every photo came out in pale grey — hair and clothing artwork
    /// is greyscale and the tint is what makes it hair. Nothing failed: the parts all resolved, and
    /// only measuring the rendered pixels against the client's own showed browns on one side and
    /// flat greys on the other.
    /// </remarks>
    [Theory]
    [InlineData("\"0x423024\"", 0x423024)]
    [InlineData("\"0XFFFFFF\"", 0xFFFFFF)]
    [InlineData("\"0x000000\"", 0x000000)]
    public void ASpriteColourWrittenAsPrefixedHex_IsRead(string json, int expected)
    {
        CameraSceneDecodeResult result = Decode(Scene(spriteColorJson: json));

        result.Accepted.Should().BeTrue();
        result.Scene!.Sprites.Should().ContainSingle().Which.Color.Should().Be(expected);
    }

    [Fact]
    public void ASpriteColourThatIsNeither_LeavesTheSpriteUntinted()
    {
        // Dropped, not thrown on, and not fatal to the photo: a sprite without a tint is a sprite.
        CameraSceneDecodeResult result = Decode(Scene(spriteColorJson: "\"rouge\""));

        result.Accepted.Should().BeTrue();
        result.Scene!.Sprites.Should().ContainSingle().Which.Color.Should().BeNull();
    }

    /// <summary>
    /// Every other number the scene carries, in the shape that used to throw. None of these is a
    /// thing a client sends; all of them are a thing a payload can say.
    /// </summary>
    [Fact]
    public void EveryNumberInTheSceneMayArriveQuoted()
    {
        // roomid, timestamp and zoom all written as strings at once. Each was its own unguarded
        // read, and each would have thrown the same way the colour did.
        CameraSceneDecodeResult result = Decode(Scene(zoom: 2, quoteNumbers: true));

        result.Accepted.Should().BeTrue();
        result.Scene!.RoomId.Should().Be(ROOM);
        result.Scene.Zoom.Should().Be(2);
    }

    /// <summary>A value that is neither a number nor a number in quotes is refused, not thrown on.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"not a number\"")]
    [InlineData("null")]
    public void ARoomIdThatIsNotANumber_IsRefused(string json)
    {
        CameraRenderRefusal refusal = Decode(Scene(roomIdJson: json)).Refusal;

        refusal.Should().Be(CameraRenderRefusal.IncompleteScene);
    }

    private static CameraSceneLimits WithAllowedHost() =>
        CameraSceneLimits.Defaults with
        {
            ExternalImagePrefixes = ["http://assets.example/c_images/"],
        };

    private static CameraSceneDecodeResult Decode(string json, CameraSceneLimits? limits = null) =>
        CameraSceneDecoder.Decode(Deflate(json), ROOM, Now, limits ?? CameraSceneLimits.Defaults);

    private static byte[] Deflate(string text)
    {
        using MemoryStream output = new();

        using (ZLibStream zlib = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            zlib.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>The document the client assembles in <c>compressData</c>, in the same order.</summary>
    private static string Scene(
        int roomId = ROOM,
        long? timestamp = null,
        int planeCount = 1,
        int spriteCount = 1,
        string spriteName = "chair_a_0",
        int? zoom = null,
        string? spriteColorJson = null,
        string? roomIdJson = null,
        bool quoteNumbers = false
    )
    {
        string plane =
            """{"z":0,"cornerPoints":[{"x":0,"y":0},{"x":10,"y":0},{"x":10,"y":10},{"x":0,"y":10}],"color":16777215,"masks":[{"name":"window_mask","point":{"x":1,"y":2},"flipH":false,"flipV":false}],"texCols":[{"assetNames":["floor_a"]}],"bottomAligned":true}""";

        // Serialized, not interpolated: a name with a backslash in it has to reach the decoder as
        // a valid JSON string, or the test proves the parser rejects its own fixture.
        string colorPart = spriteColorJson is null ? "" : $$""","color":{{spriteColorJson}}""";
        string sprite =
            $$"""{"name":{{JsonSerializer.Serialize(spriteName)}},"x":5,"y":6,"z":1.5{{colorPart}}}""";

        string planes = string.Join(",", Enumerable.Repeat(plane, planeCount));
        string sprites = string.Join(",", Enumerable.Repeat(sprite, spriteCount));
        long stamp = timestamp ?? new DateTimeOffset(Now).ToUnixTimeMilliseconds();

        // A bare number, or the same number in quotes — the served client writes both shapes.
        string Number(object value) => quoteNumbers ? $"\"{value}\"" : value.ToString()!;

        string room = roomIdJson ?? Number(roomId);
        string zoomPart = zoom is null ? "" : $""" ,"zoom" : {Number(zoom)}""";

        return $$"""
            { "planes" : [{{planes}}] ,"sprites" : [{{sprites}}] ,"modifiers" : {} ,"filters" : [] ,"roomid" : {{room}}{{zoomPart}} ,"status" : 7 ,"timestamp" : {{Number(
                stamp
            )}} ,"checksum" : 12345 }
            """;
    }
}
