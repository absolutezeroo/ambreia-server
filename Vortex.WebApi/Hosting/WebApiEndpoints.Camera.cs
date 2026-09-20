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

    /// <summary>
    /// Must agree with the client's <c>navigator.thumbnail.url_base</c>, which it concatenates with
    /// the room id and <c>.png</c> and nothing else — so that setting has to end exactly here, at
    /// the slash.
    /// </summary>
    private const string RoomThumbnailRoute = "/api/public/camera/thumbnails/{roomId:int}.png";

    /// <summary>
    /// A selfie, at the url the client builds for one — in both sizes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every segment of this is the client's, not a choice. Anything whose type is not a photo
    /// poster has <c>postcards/selfie/</c> put in front of its stuff-data url, so the shape is
    /// <c>stories.image_url_base</c> + <c>postcards/selfie/</c> + <c>camera.url_path_prefix</c> +
    /// the key + <c>.png</c>, and this route is the last three with the prefix at its default.
    /// </para>
    /// <para>
    /// The two sizes share one route rather than splitting on a literal <c>_small.png</c> suffix,
    /// because both patterns match <c>&lt;key&gt;_small.png</c> — once as the suffix and once as a
    /// key that happens to end in <c>_small</c> — and which of them wins is routing precedence
    /// rather than a decision anybody made. Here it is a decision: the frame on the wall asks for
    /// the small copy (<c>buildThumbnailUrl</c> rewrites <c>.png</c>), the viewer that opens when
    /// it is clicked asks for the photo whole (<c>getImageUrl</c> does not).
    /// </para>
    /// </remarks>
    private const string SelfiePhotoRoute =
        "/api/public/camera/postcards/selfie/photos/{photoKey}.png";

    /// <summary>What the client appends to ask for the wall-sized copy.</summary>
    private const string SmallSuffix = "_small";

    public static void MapCamera(WebApplication app)
    {
        app.MapGet(
                RoomThumbnailRoute,
                async Task<IResult> (
                    int roomId,
                    [FromServices] ICameraPhotoStore photos,
                    CancellationToken ct
                ) =>
                {
                    ReadOnlyMemory<byte> png = await photos
                        .ReadRoomThumbnailAsync(roomId, ct)
                        .ConfigureAwait(false);

                    // A 404 is the ordinary answer here rather than a fault: the client asks this
                    // for every room in a search result, and most rooms have never had a picture
                    // taken of them.
                    return png.IsEmpty ? Results.NotFound() : Results.Bytes(png, "image/png");
                }
            )
            .WithName("GetRoomThumbnail")
            .WithTags("Camera")
            .AllowAnonymous()
            .RequireCors(WebApiAppConfigurator.PublicAssetCorsPolicyName);

        app.MapGet(
                SelfiePhotoRoute,
                async Task<IResult> (
                    string photoKey,
                    [FromServices] ICameraPhotoStore photos,
                    CancellationToken ct
                ) =>
                {
                    bool small = photoKey.EndsWith(SmallSuffix, StringComparison.Ordinal);

                    if (small)
                    {
                        photoKey = photoKey[..^SmallSuffix.Length];
                    }

                    ReadOnlyMemory<byte> png = await (
                        small
                            ? photos.ReadSmallPhotoAsync(photoKey, ct)
                            : photos.ReadPhotoAsync(photoKey, ct)
                    ).ConfigureAwait(false);

                    return png.IsEmpty ? Results.NotFound() : Results.Bytes(png, "image/png");
                }
            )
            .WithName("GetSelfiePhoto")
            .WithTags("Camera")
            .AllowAnonymous()
            .RequireCors(WebApiAppConfigurator.PublicAssetCorsPolicyName);

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
            .AllowAnonymous()
            // The client reads this with fetch() rather than an <img>, so the browser applies CORS
            // to it — and the default policy answers nothing to an origin the operator has not
            // listed. This one is open and uncredentialed, which is what a public image wants.
            .RequireCors(WebApiAppConfigurator.PublicAssetCorsPolicyName);
    }
}
