using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Primitives.Camera;
using Vortex.Rooms.Camera.Render;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// Where a photo's sprites are allowed to come from, and how often.
/// </summary>
/// <remarks>
/// The allowlist decided which hosts may be reached and the decoder refuses a key with a separator
/// in it. This is the second lock on both: a name that escapes the asset directory is refused here
/// too, and the number of requests one photo may make is bounded here because nothing upstream
/// counts them.
/// </remarks>
public sealed class CameraSpriteResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "vortex-camera-sprites-" + Guid.NewGuid().ToString("N")
    );

    public CameraSpriteResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public async Task ALocalSpriteIsReadFromTheAssetDirectoryAsync()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "chair_a_0.png"), [1, 2, 3]);

        IReadOnlyDictionary<string, byte[]> resolved = await ResolveAsync(Local("chair_a_0"));

        resolved.Should().ContainKey("chair_a_0").WhoseValue.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task ALocalSpriteThatIsNotThere_IsSimplyAbsentAsync()
    {
        // A pack one frame short leaves a hole in the photo, which beats refusing to take it.
        IReadOnlyDictionary<string, byte[]> resolved = await ResolveAsync(Local("never_extracted"));

        resolved.Should().BeEmpty();
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("../../etc/passwd")]
    public async Task ANameThatClimbsOutOfTheAssetDirectory_IsRefusedAsync(string key)
    {
        // The decoder already refuses a separator in a key. This is the lock that still holds the
        // day somebody joins a name to a path without asking it first.
        await File.WriteAllBytesAsync(
            Path.Combine(Path.GetDirectoryName(_root)!, "secrets.png"),
            [9, 9, 9]
        );

        IReadOnlyDictionary<string, byte[]> resolved = await ResolveAsync(Local(key));

        resolved.Should().BeEmpty();
    }

    [Fact]
    public async Task OneSpriteNamedManyTimes_IsReadOnceAsync()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "floor.png"), [7]);

        IReadOnlyDictionary<string, byte[]> resolved = await ResolveAsync(
            Local("floor"),
            Local("floor"),
            Local("floor")
        );

        // A tiled floor names one asset a hundred times; reading it a hundred times would be the
        // whole cost of a render.
        resolved.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExternalImagesAreFetchedUpToTheBudgetAndNoFurtherAsync()
    {
        using CountingHandler handler = new();

        IReadOnlyDictionary<string, byte[]> resolved = await ResolveAsync(
            handler,
            maxFetches: 2,
            External("https://assets.test/a.png"),
            External("https://assets.test/b.png"),
            External("https://assets.test/c.png")
        );

        // One photo must not become a burst against the asset host. Past the budget it simply loses
        // its remaining badges -- the shape the client already makes past thirty external images.
        handler.Requests.Should().Be(2);
        resolved.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnExternalImageBiggerThanAllowed_IsDroppedAsync()
    {
        using CountingHandler handler = new() { Body = new byte[4096] };

        IReadOnlyDictionary<string, byte[]> resolved = await ResolveAsync(
            handler,
            maxFetches: 4,
            maxBytes: 64,
            External("https://assets.test/huge.png")
        );

        resolved.Should().BeEmpty();
    }

    [Fact]
    public async Task AnExternalImageThatAnswersAnError_IsDroppedAsync()
    {
        using CountingHandler handler = new() { Status = HttpStatusCode.NotFound };

        IReadOnlyDictionary<string, byte[]> resolved = await ResolveAsync(
            handler,
            maxFetches: 4,
            External("https://assets.test/gone.png")
        );

        resolved.Should().BeEmpty();
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> ResolveAsync(
        params CameraSceneSprite[] sprites
    )
    {
        using CountingHandler handler = new();

        return await ResolveAsync(handler, maxFetches: 0, sprites);
    }

    private Task<IReadOnlyDictionary<string, byte[]>> ResolveAsync(
        CountingHandler handler,
        int maxFetches,
        params CameraSceneSprite[] sprites
    ) => ResolveAsync(handler, maxFetches, 1024 * 1024, sprites);

    private async Task<IReadOnlyDictionary<string, byte[]>> ResolveAsync(
        CountingHandler handler,
        int maxFetches,
        int maxBytes,
        params CameraSceneSprite[] sprites
    )
    {
        // The scope that made the handler disposes it, so this must not do it a second time.
        using HttpClient http = new(handler, disposeHandler: false);

        // A library with nowhere to fetch from: these tests are about the directory and the
        // allowlist, and the pack answers null before it looks at anything when no url is set.
        NitroSpriteLibrary library = new(
            _ => Task.FromResult<IReadOnlyCollection<string>>([]),
            http,
            NullLogger.Instance
        );

        using CameraSpriteResolver resolver = new(_root, http, library, NullLogger.Instance);

        CameraRenderLimits limits = CameraRenderLimits.Defaults with
        {
            MaxExternalFetches = maxFetches,
            ExternalTimeout = TimeSpan.FromSeconds(5),
            MaxExternalBytes = maxBytes,
        };

        return await resolver.ResolveAsync([.. sprites], [], limits, CancellationToken.None);
    }

    private static CameraSceneSprite Local(string name) =>
        Sprite(name, CameraSpriteSource.LocalAsset);

    private static CameraSceneSprite External(string url) =>
        Sprite(url, CameraSpriteSource.ExternalImage);

    private static CameraSceneSprite Sprite(string name, CameraSpriteSource source) =>
        new()
        {
            Name = name,
            Source = source,
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

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public byte[] Body { get; init; } = [1, 2, 3, 4];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;

            return Task.FromResult(
                new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) }
            );
        }
    }
}
