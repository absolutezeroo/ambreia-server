using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Vortex.WebApi.Http;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The website's photo albums.
/// </summary>
/// <remarks>
/// No delete route: an album is deleted by deleting its card, through the feed's own
/// <c>DELETE /api/user/feed/{id}</c> and its second-factor gate. See <see cref="IWebApiAlbumService"/>.
/// </remarks>
internal static class WebApiEndpointsAlbum
{
    public static void MapAlbums(WebApplication app, string tagPublic, string tagUser)
    {
        app.MapGet(
                "/api/public/albums/{id:int}",
                async Task<Results<Ok<Album>, NotFound<ApiErrorResponse>>> (
                    HttpContext ctx,
                    int id,
                    [FromServices] IWebApiAlbumService albums,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? viewerId, _) = await WebApiEndpoints
                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    Album? album = await albums.GetAsync(id, viewerId, ct).ConfigureAwait(false);

                    return album is null
                        ? TypedResults.NotFound(new ApiErrorResponse("album_not_found"))
                        : TypedResults.Ok(album);
                }
            )
            .WithName("Album")
            .WithSummary("One album and its photos.")
            .WithTags(tagPublic);

        app.MapGet(
                "/api/public/users/{uniqueId}/albums",
                async Task<Ok<IReadOnlyList<FeedAlbum>>> (
                    string uniqueId,
                    [FromServices] IWebApiAlbumService albums,
                    CancellationToken ct
                ) =>
                    // An unparseable id answers an empty list, like the photo section beside it.
                    int.TryParse(uniqueId, out int ownerId)
                        ? TypedResults.Ok(await albums.ListAsync(ownerId, ct).ConfigureAwait(false))
                        : TypedResults.Ok<IReadOnlyList<FeedAlbum>>([])
            )
            .WithName("PlayerAlbums")
            .WithSummary("One player's albums, newest first.")
            .WithTags(tagPublic);

        app.MapPost(
                "/api/user/albums",
                async Task<
                    Results<
                        Ok<Album>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    AlbumCreateRequest body,
                    [FromServices] IWebApiAlbumService albums,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                    await WriteAsync(
                            ctx,
                            sessions,
                            players,
                            playerId => albums.CreateAsync(playerId, body, ct),
                            ct
                        )
                        .ConfigureAwait(false)
            )
            // It posts a card, so it spends the feed's publishing budget.
            .RequireRateLimiting(WebApiEndpoints.FeedPostRateLimitPolicy)
            .WithName("AlbumCreate")
            .WithSummary("Create an album and post its card to the feed.")
            .WithTags(tagUser);

        app.MapPut(
                "/api/user/albums/{id:int}",
                async Task<
                    Results<
                        Ok<Album>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    int id,
                    AlbumEditRequest body,
                    [FromServices] IWebApiAlbumService albums,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                    await WriteAsync(
                            ctx,
                            sessions,
                            players,
                            playerId => albums.EditAsync(playerId, id, body, ct),
                            ct
                        )
                        .ConfigureAwait(false)
            )
            .WithName("AlbumEdit")
            .WithSummary("Rename one of your albums.")
            .WithTags(tagUser);

        app.MapPost(
                "/api/user/albums/{id:int}/photos",
                async Task<
                    Results<
                        Ok<Album>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    int id,
                    AlbumPhotosRequest body,
                    [FromServices] IWebApiAlbumService albums,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                    await WriteAsync(
                            ctx,
                            sessions,
                            players,
                            playerId => albums.AddPhotosAsync(playerId, id, body, ct),
                            ct
                        )
                        .ConfigureAwait(false)
            )
            // It bumps the card to the top of everyone's feed.
            .RequireRateLimiting(WebApiEndpoints.FeedPostRateLimitPolicy)
            .WithName("AlbumAddPhotos")
            .WithSummary("Add photos to one of your albums; its card comes back up.")
            .WithTags(tagUser);

        app.MapDelete(
                "/api/user/albums/{id:int}/photos/{photoKey}",
                async Task<
                    Results<
                        Ok<Album>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    int id,
                    string photoKey,
                    [FromServices] IWebApiAlbumService albums,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                    await WriteAsync(
                            ctx,
                            sessions,
                            players,
                            playerId => albums.RemovePhotoAsync(playerId, id, photoKey, ct),
                            ct
                        )
                        .ConfigureAwait(false)
            )
            .WithName("AlbumRemovePhoto")
            .WithSummary("Take one photo out of one of your albums.")
            .WithTags(tagUser);
    }

    /// <summary>
    /// The four writes' shared shape: a player behind the session, then the refusal to a status.
    /// </summary>
    private static async Task<
        Results<
            Ok<Album>,
            BadRequest<ApiErrorResponse>,
            NotFound<ApiErrorResponse>,
            UnauthorizedError
        >
    > WriteAsync(
        HttpContext ctx,
        WebApiSessionStore sessions,
        IWebApiPlayerService players,
        Func<int, Task<(AlbumRefusal Refusal, Album? Album)>> write,
        CancellationToken ct
    )
    {
        (int? playerId, IResult? refused) = await WebApiEndpoints
            .SelectedPlayerAsync(ctx, sessions, players, ct)
            .ConfigureAwait(false);

        if (playerId is null)
        {
            return refused is UnauthorizedError unauthorized
                ? unauthorized
                : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
        }

        (AlbumRefusal refusal, Album? album) = await write(playerId.Value).ConfigureAwait(false);

        // One 404 for "no such album", "deleted" and "not yours", as the feed's delete answers:
        // telling them apart would let a caller probe whose albums exist.
        return refusal switch
        {
            AlbumRefusal.None when album is not null => TypedResults.Ok(album),
            AlbumRefusal.None or AlbumRefusal.NotFound => TypedResults.NotFound(
                new ApiErrorResponse("album_not_found")
            ),
            _ => TypedResults.BadRequest(new ApiErrorResponse(Code(refusal))),
        };
    }

    private static string Code(AlbumRefusal refusal) =>
        refusal switch
        {
            AlbumRefusal.TitleInvalid => "album_title_invalid",
            AlbumRefusal.DescriptionTooLong => "album_description_too_long",
            AlbumRefusal.Empty => "album_empty",
            AlbumRefusal.PhotoNotYours => "album_photo_not_yours",
            AlbumRefusal.Full => "album_full",
            AlbumRefusal.WordFiltered => "album_word_filtered",
            _ => "album_invalid",
        };
}
