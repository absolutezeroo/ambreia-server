using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vortex.Primitives.Camera;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// Serving a room photo.
/// </summary>
/// <remarks>
/// <para>
/// A route rather than a static directory, and that is the whole security posture: the photo
/// directory is never handed to a web server to walk. A request names a key, the store decides
/// whether that key is one it minted, and nothing outside <c>ICameraPhotoStore</c> ever turns a
/// request into a path.
/// </para>
/// <para>
/// Public on purpose. The client fetches this with no session — it is an image tag's worth of
/// authority — so the key is what guards a photo, which is why it is thirty-two random hex
/// characters rather than a number.
/// </para>
/// </remarks>
internal static partial class WebApiEndpointsCamera
{
    /// <summary>
    /// Must agree with <c>camera.url_path_prefix</c> and with the client's own
    /// <c>stories.image_url_base</c>: the client concatenates the three, so a photo is fetched from
    /// wherever those two settings say and nowhere else.
    /// </summary>
    private const string CameraPhotoRoute = "/api/public/camera/photos/{photoKey}.png";

    public static void MapCamera(WebApplication app)
    {
        app.MapGet(
                CameraPhotoRoute,
                async Task<IResult> (
                    string photoKey,
                    // [FromServices] rather than left to inference. A service the WebApi container
                    // does not know is inferred as a request body, a GET may not have one, and the
                    // whole route table then fails to build -- so one unregistered dependency takes
                    // down every endpoint in the host rather than this one.
                    [FromServices] ICameraPhotoStore photos,
                    CancellationToken ct
                ) =>
                {
                    ReadOnlyMemory<byte> png = await photos
                        .ReadPhotoAsync(photoKey, ct)
                        .ConfigureAwait(false);

                    if (png.IsEmpty)
                    {
                        return Results.NotFound();
                    }

                    // The content type is stated, never sniffed: this is the one route that answers
                    // with bytes a player's own client produced the description of.
                    return Results.Bytes(png, "image/png");
                }
            )
            .WithName("GetCameraPhoto")
            .WithTags("Camera")
            .AllowAnonymous();
    }
}
