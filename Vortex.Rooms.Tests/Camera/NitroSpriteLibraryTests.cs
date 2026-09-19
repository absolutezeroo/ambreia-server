using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Vortex.Primitives.Camera;
using Vortex.Rooms.Camera.Render;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// An asset name, out of the hotel's own pack, into pixels.
/// </summary>
/// <remarks>
/// Served from the real <c>duck.nitro</c> fixture, so what is asserted is the whole chain the camera
/// depends on: the classname out of the asset name, the bundle, the manifest's indirection, the crop
/// off the sheet and the mirror. Every link of it was wrong at some point today.
/// </remarks>
public sealed class NitroSpriteLibraryTests
{
    private static readonly string[] ClassNames = ["duck", "chair", "chair_basic"];

    private static byte[] DuckBundle() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Furniture/Fixtures/duck.nitro"));

    [Fact]
    public async Task AnAssetNameBecomesItsOwnPng()
    {
        using Serving serving = new(DuckBundle());
        using HttpClient http = new(serving, disposeHandler: false);

        byte[]? sprite = await Library(http)
            .GetSpriteAsync("duck_64_a_6_0", Limits(), CancellationToken.None);

        sprite.Should().NotBeNull();

        using SKBitmap decoded = SKBitmap.Decode(sprite);

        // The frame the manifest gives for this asset, and therefore the size of the crop.
        decoded.Width.Should().Be(22);
        decoded.Height.Should().Be(23);
    }

    /// <summary>
    /// The mirrored direction. <c>duck_64_a_0_0</c> has no pixels of its own — five of this bundle's
    /// nine assets do not — so this is the same rectangle, turned.
    /// </summary>
    [Fact]
    public async Task AMirroredAssetIsTheSameSizeAndTheOtherWayRound()
    {
        using Serving serving = new(DuckBundle());
        using HttpClient http = new(serving, disposeHandler: false);
        NitroSpriteLibrary library = Library(http);

        byte[]? original = await library.GetSpriteAsync(
            "duck_64_a_6_0",
            Limits(),
            CancellationToken.None
        );
        byte[]? mirrored = await library.GetSpriteAsync(
            "duck_64_a_0_0",
            Limits(),
            CancellationToken.None
        );

        using SKBitmap a = SKBitmap.Decode(original);
        using SKBitmap b = SKBitmap.Decode(mirrored);

        b.Width.Should().Be(a.Width);
        b.Height.Should().Be(a.Height);

        // Turned, not merely re-encoded: a mirror that quietly did nothing would pass every
        // assertion above and draw every chair in the room facing the wrong way.
        b.GetPixel(0, a.Height / 2).Should().Be(a.GetPixel(a.Width - 1, a.Height / 2));
    }

    /// <summary>
    /// The guard that keeps a payload from becoming a request generator: a scene may name thousands
    /// of sprites, and a name matching no classname this hotel sells never leaves the process.
    /// </summary>
    [Fact]
    public async Task AnAssetNoClassnameClaims_IsNeverFetched()
    {
        using Serving serving = new(DuckBundle());
        using HttpClient http = new(serving, disposeHandler: false);

        byte[]? sprite = await Library(http)
            .GetSpriteAsync("not_a_real_furni_64_a_0_0", Limits(), CancellationToken.None);

        sprite.Should().BeNull();
        serving.Requests.Should().Be(0);
    }

    /// <summary>
    /// Longest match wins. <c>chair</c> and <c>chair_basic</c> are both real classnames, and
    /// <c>chair_basic_64_a_0_0</c> belongs to the second — matching the first would fetch the wrong
    /// bundle and find nothing in it.
    /// </summary>
    [Fact]
    public async Task TheLongestClassnameWins()
    {
        using Serving serving = new(DuckBundle());

        using HttpClient http = new(serving, disposeHandler: false);

        await Library(http)
            .GetSpriteAsync("chair_basic_64_a_0_0", Limits(), CancellationToken.None);

        serving.LastPath.Should().EndWith("chair_basic.nitro");
    }

    /// <summary>A bundle is fetched once, however many sprites a photo takes out of it.</summary>
    [Fact]
    public async Task ABundleIsFetchedOncePerClassname()
    {
        using Serving serving = new(DuckBundle());
        using HttpClient http = new(serving, disposeHandler: false);
        NitroSpriteLibrary library = Library(http);

        foreach (string name in new[] { "duck_64_a_0_0", "duck_64_a_2_0", "duck_64_a_6_0" })
        {
            await library.GetSpriteAsync(name, Limits(), CancellationToken.None);
        }

        serving.Requests.Should().Be(1);
    }

    /// <summary>
    /// A missing bundle stays missing. Re-asking would turn one absent furniture into a request per
    /// photo for the life of the process.
    /// </summary>
    [Fact]
    public async Task AMissingBundleIsNotAskedForTwice()
    {
        using Serving serving = new(DuckBundle()) { Status = HttpStatusCode.NotFound };
        using HttpClient http = new(serving, disposeHandler: false);
        NitroSpriteLibrary library = Library(http);

        await library.GetSpriteAsync("duck_64_a_0_0", Limits(), CancellationToken.None);
        await library.GetSpriteAsync("duck_64_a_2_0", Limits(), CancellationToken.None);

        serving.Requests.Should().Be(1);
    }

    /// <summary>Nothing is fetched at all until an operator says where the pack is.</summary>
    [Fact]
    public async Task WithNoAssetUrlConfigured_NothingIsFetched()
    {
        using Serving serving = new(DuckBundle());
        using HttpClient http = new(serving, disposeHandler: false);

        byte[]? sprite = await Library(http)
            .GetSpriteAsync("duck_64_a_0_0", Limits(url: ""), CancellationToken.None);

        sprite.Should().BeNull();
        serving.Requests.Should().Be(0);
    }

    /// <summary>
    /// The client is the caller's, as it is in production: the resolver owns it and the library
    /// borrows it, so the scope that made the handler is the scope that closes everything.
    /// </summary>
    private static NitroSpriteLibrary Library(HttpClient http) =>
        new(
            _ => Task.FromResult<IReadOnlyCollection<string>>(ClassNames),
            http,
            NullLogger.Instance
        );

    private static CameraRenderLimits Limits(string url = "https://assets.test/dcr/hof_furni/") =>
        CameraRenderLimits.Defaults with
        {
            FurnitureAssetUrl = url,
        };

    private sealed class Serving(byte[] bundle) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public string LastPath { get; private set; } = string.Empty;

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            LastPath = request.RequestUri!.AbsolutePath;

            return Task.FromResult(
                new HttpResponseMessage(Status) { Content = new ByteArrayContent(bundle) }
            );
        }
    }
}
