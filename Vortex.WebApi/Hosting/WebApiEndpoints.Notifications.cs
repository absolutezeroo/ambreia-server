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
/// What the hotel has told this player.
/// </summary>
/// <remarks>
/// Both routes are a player's own and neither is public — unlike the feed, where the reads are
/// anonymous. A notification has exactly one reader.
/// </remarks>
internal static partial class WebApiEndpointsNotifications
{
    public static void MapNotifications(WebApplication app, string tagUser)
    {
        app.MapGet(
                "/api/user/notifications",
                async Task<
                    Results<Ok<NotificationPage>, NotFound<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    int? page,
                    int? pageSize,
                    [FromServices] IWebApiNotificationService notifications,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refused) = await WebApiEndpoints
                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        // A notification belongs to a PLAYER, so an account that owns no avatar
                        // has no list rather than an empty one.
                        return refused is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    return TypedResults.Ok(
                        await notifications
                            .GetAsync(playerId.Value, page ?? 1, pageSize ?? 0, ct)
                            .ConfigureAwait(false)
                    );
                }
            )
            .WithName("Notifications")
            .WithSummary("My notifications, newest first, with the unread count.")
            .WithTags(tagUser);

        // POST and not PUT: "I have looked at these" is an event, and what it marks depends on
        // when it arrives rather than on anything the caller names.
        app.MapPost(
                "/api/user/notifications/read",
                async Task<
                    Results<
                        Ok<NotificationsReadResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    [FromServices] IWebApiNotificationService notifications,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
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

                    return TypedResults.Ok(
                        new NotificationsReadResponse(
                            await notifications
                                .MarkSeenAsync(playerId.Value, ct)
                                .ConfigureAwait(false)
                        )
                    );
                }
            )
            .WithName("NotificationsRead")
            .WithSummary("Mark every unread notification read.")
            .WithTags(tagUser);
    }
}
