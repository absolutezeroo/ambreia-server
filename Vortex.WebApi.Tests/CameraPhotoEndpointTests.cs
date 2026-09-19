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
