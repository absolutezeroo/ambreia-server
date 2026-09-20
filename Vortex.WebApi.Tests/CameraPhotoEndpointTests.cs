using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Serving a room photo.
/// </summary>
/// <remarks>
/// The route existed, was mapped, and answered 500 to everyone: <c>ICameraPhotoStore</c> was
/// registered in the Orleans root container but never forwarded into the web API's own one. Nothing
/// failed at startup, because a <c>[FromServices]</c> parameter is not inferred as the request body —
/// that is the loud half of the trap, and this is the quiet half. The photo was taken, stored and
/// billed, and the url in the client's hands returned an error.
/// <para>
/// So the first assertion here is not really about content type. It is that the route can be reached
/// at all with the container the host actually builds.
/// </para>
/// </remarks>
public sealed class CameraPhotoEndpointTests
{
    [Fact]
    public async Task AStoredPhoto_IsServedAsAPng()
    {
        await using WebApiTestFactory factory = new();

        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];
        string key = factory.Photos.Add(png);

        using HttpResponseMessage response = await factory.Client.GetAsync(
            $"/api/public/camera/photos/{key}.png"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(png);
    }

    [Fact]
    public async Task APhotoNobodyTook_IsNotFound()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync(
            "/api/public/camera/photos/0123456789ABCDEF0123456789ABCDEF.png"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The client reads a photo with <c>fetch()</c> from its own origin, so the browser applies CORS
    /// to it. The default policy is credentialed — it cannot answer a wildcard, and it answers
    /// nothing at all to an origin the operator has not listed — which is how a photo that was taken,
    /// stored, billed and served with a 200 still failed to appear.
    /// </summary>
    [Fact]
    public async Task APhoto_IsReadableFromAnOriginNobodyListed()
    {
        await using WebApiTestFactory factory = new();

        string key = factory.Photos.Add([1, 2, 3]);

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"/api/public/camera/photos/{key}.png"
        );
        request.Headers.Add("Origin", "https://some-other-hotel.example");

        using HttpResponseMessage response = await factory.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response
            .Headers.GetValues("Access-Control-Allow-Origin")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("*");
    }

    /// <summary>
    /// The half that matters more. An open policy is only safe while it stays on the routes that
    /// carry no session, so this asserts the credentialed default is still the default: the same
    /// unlisted origin gets nothing from a route that is not the photo.
    /// </summary>
    [Fact]
    public async Task TheOpenPolicy_DoesNotReachTheRestOfTheApi()
    {
        await using WebApiTestFactory factory = new();

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/public/chain");
        request.Headers.Add("Origin", "https://some-other-hotel.example");

        using HttpResponseMessage response = await factory.Client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    /// <summary>
    /// A room's thumbnail is addressed by the room, not by a key.
    /// </summary>
    /// <remarks>
    /// That is the whole design, and it is the client's: with no official picture, the navigator
    /// asks for <c>navigator.thumbnail.url_base</c> + the room id + <c>.png</c>. Get this url shape
    /// wrong and nothing anywhere fails — the thumbnail simply never appears, which looks exactly
    /// like a room nobody ever photographed.
    /// </remarks>
    [Fact]
    public async Task ARoomsThumbnail_IsServedByItsRoomId()
    {
        await using WebApiTestFactory factory = new();

        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 7, 7];
        factory.Photos.AddRoomThumbnail(42, png);

        using HttpResponseMessage response = await factory.Client.GetAsync(
            "/api/public/camera/thumbnails/42.png"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(png);
    }

    /// <summary>
    /// A photo hanging on a wall is fetched from a url the client builds, not one the server chose.
    /// </summary>
    /// <remarks>
    /// <c>FurnitureExternalImageVisualization</c> prefixes the photo's stuff-data url with
    /// <c>postcards/selfie/</c> and rewrites <c>.png</c> to <c>_small.png</c>. Every segment below
    /// is therefore fixed by the client; get one wrong and the frame on the wall stays empty with
    /// nothing failing anywhere.
    /// </remarks>
    [Fact]
    public async Task APhotoOnAWall_IsServedFromTheUrlTheFurnitureBuilds()
    {
        await using WebApiTestFactory factory = new();

        string key = factory.Photos.Add([0x89, (byte)'P', (byte)'N', (byte)'G', 4, 5]);

        using HttpResponseMessage response = await factory.Client.GetAsync(
            $"/api/public/camera/postcards/selfie/photos/{key}_small.png"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
    }

    [Fact]
    public async Task ASmallCopyOfAPhotoNobodyTook_IsNotFound()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync(
            "/api/public/camera/postcards/selfie/photos/0123456789ABCDEF0123456789ABCDEF_small.png"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The ordinary case, asked once per room in every search result.</summary>
    [Fact]
    public async Task ARoomNobodyPhotographed_IsNotFound()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync(
            "/api/public/camera/thumbnails/999.png"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A key the store did not mint is refused by the store, not by the route — which is the point of
    /// the arrangement. Nothing outside <c>ICameraPhotoStore</c> turns a url segment into a path.
    /// </summary>
    [Theory]
    [InlineData("not-a-key")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789ABCDEF0123456789ABCDE")]
    public async Task AKeyTheStoreNeverMinted_IsNotFound(string key)
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync(
            $"/api/public/camera/photos/{key}.png"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
