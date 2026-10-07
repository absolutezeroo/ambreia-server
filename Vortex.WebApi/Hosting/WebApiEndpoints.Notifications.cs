using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Vortex.WebApi.Http;
using Vortex.WebApi.Notifications;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// What the hotel has told this player.
/// </summary>
/// <remarks>
/// Both routes are a player's own and neither is public: a notification has exactly one reader.
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

        // Server-sent events, not a WebSocket and not polling.
        //
        // One direction is all this needs — the hotel tells, the tab listens — and SSE is that
        // over the connection the site already has: same origin, same cookie, same proxy, and the
        // browser reconnects on its own when it drops. A WebSocket would be a second protocol to
        // authenticate and keep alive for a feature that never sends anything upstream.
        //
        // Deployment note, because it is invisible until it bites: an nginx in front of this
        // needs `proxy_buffering off` on the route, or it holds each event in its buffer and the
        // stream arrives in silent lumps.
        app.MapGet(
                "/api/user/notifications/stream",
                async Task (
                    HttpContext ctx,
                    [FromServices] IWebApiNotificationService notifications,
                    [FromServices] NotificationBroadcaster broadcaster,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, _) = await WebApiEndpoints
                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        // Written by hand rather than returned: this route's return type is the
                        // stream itself, so there is no Results<> union to put a refusal in.
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;

                        return;
                    }

                    ctx.Response.Headers.ContentType = "text/event-stream";
                    ctx.Response.Headers.CacheControl = "no-cache";

                    // Tells any intermediary that still buffers by default not to. Harmless where
                    // it is not understood, and the difference between "live" and "in lumps"
                    // where it is.
                    ctx.Response.Headers["X-Accel-Buffering"] = "no";

                    using NotificationBroadcaster.Subscription subscription = broadcaster.Subscribe(
                        playerId.Value
                    );

                    // The current count first, before anything is waited on. A tab that opens
                    // after the notification was written would otherwise show nothing until the
                    // next one arrived.
                    await WriteAsync(
                            ctx,
                            (
                                await notifications
                                    .GetAsync(playerId.Value, 1, 1, ct)
                                    .ConfigureAwait(false)
                            ).Unread,
                            ct
                        )
                        .ConfigureAwait(false);

                    try
                    {
                        await foreach (
                            int unread in subscription.Reader.ReadAllAsync(ct).ConfigureAwait(false)
                        )
                        {
                            await WriteAsync(ctx, unread, ct).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // The tab closed. The only way this loop ever ends, and not a fault: the
                        // `using` above is what actually unsubscribes.
                    }
                }
            )
            .WithName("NotificationsStream")
            .WithSummary("A live count of unread notifications, as server-sent events.")
            .WithTags(tagUser);
    }

    /// <summary>
    /// One SSE frame: the unread count, flushed immediately.
    /// </summary>
    /// <remarks>
    /// The flush is the whole point. Without it the response sits in Kestrel's buffer until it
    /// fills or the request ends — and this request only ends when the tab closes, so nothing
    /// would ever arrive.
    /// </remarks>
    private static async Task WriteAsync(HttpContext ctx, int unread, CancellationToken ct)
    {
        await ctx
            .Response.WriteAsync($"data: {unread.ToString(CultureInfo.InvariantCulture)}\n\n", ct)
            .ConfigureAwait(false);

        await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }
}
