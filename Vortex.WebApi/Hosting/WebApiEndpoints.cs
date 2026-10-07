using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Observability;
using Vortex.WebApi.Http;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// Maps the client-facing web API onto minimal-API endpoints — one handler per route — preserving the
/// exact paths, methods, status codes and response shapes of the previous <c>HttpListener</c>
/// dispatcher. Authentication is the same cookie-backed session model (see
/// <see cref="WebApiHttpContextExtensions"/>); the sensitive endpoints declare a named rate-limiting
/// policy. Every endpoint is tagged for Swagger grouping.
/// </summary>
internal static partial class WebApiEndpoints
{
    public const string LoginRateLimitPolicy = "webapi-login";
    public const string RegistrationRateLimitPolicy = "webapi-registration";
    public const string SsoTokenRateLimitPolicy = "webapi-ssotoken";

    private const string TagPublic = "Public";
    private const string TagAuth = "Authentication";
    private const string TagUser = "User";
    private const string TagNewUser = "NewUser";

    public static void Map(WebApplication app)
    {
        MapPublic(app);
        MapAuthentication(app);
        MapUser(app);
        MapNewUser(app);
    }

    private static void MapPublic(WebApplication app)
    {
        app.MapGet(
                "/api/public/info/hello",
                Ok<HelloResponse> () => TypedResults.Ok(new HelloResponse("ok"))
            )
            .WithName("Hello")
            .WithSummary("Server liveness probe used by the onboarding client.")
            .WithTags(TagPublic);

        app.MapGet(
                "/health",
                async (
                    RequiredServiceGuard guard,
                    IDbContextFactory<VortexDbContext> dbCtxFactory,
                    CancellationToken ct
                ) =>
                {
                    bool databaseUp;

                    try
                    {
                        await using VortexDbContext dbCtx = await dbCtxFactory
                            .CreateDbContextAsync(ct)
                            .ConfigureAwait(false);

                        databaseUp = await dbCtx.Database.CanConnectAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // A probe failure is a health signal, not an error to propagate - report it
                        // as part of the response body instead of a 500.
                        databaseUp = false;
                    }

                    string status =
                        !databaseUp ? "Unhealthy"
                        : guard.IsDegraded ? "Degraded"
                        : "Healthy";

                    HealthResponse health = new(
                        status,
                        databaseUp ? "up" : "down",
                        guard.DegradedServices
                    );

                    // The one endpoint left on Results.Json, and the one status that cannot be
                    // typed: there is no `ServiceUnavailable<T>`, so 503-with-a-body has to be built
                    // at runtime. Both answers are declared below instead — the probe is an
                    // operations surface no generated client reads, so a declaration is the right
                    // amount of truth for it.
                    return Results.Json(
                        health,
                        statusCode: databaseUp
                            ? StatusCodes.Status200OK
                            : StatusCodes.Status503ServiceUnavailable
                    );
                }
            )
            .WithName("Health")
            .WithSummary(
                "Liveness/readiness probe: database connectivity and RequiredServiceGuard's "
                    + "degraded-service state (OPS-02)."
            )
            .WithTags(TagPublic)
            .Produces<HealthResponse>()
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);
    }

    private static void MapAuthentication(WebApplication app)
    {
        app.MapPost(
                "/api/public/authentication/login",
                async Task<
                    Results<Ok<LoginResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    LoginRequest body,
                    IWebApiAuthService auth,
                    IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(
                            new ApiErrorResponse("pocket.auth.missing_credentials")
                        );
                    }

                    (bool success, string? sessionId, int accountId, string? error) =
                        await auth.LoginAsync(body.Email!, body.Password!, ct)
                            .ConfigureAwait(false);

                    if (!success)
                    {
                        return new UnauthorizedError(error ?? "pocket.auth.invalid_login");
                    }

                    ctx.IssueSessionCookie(sessionId!);

                    System.Collections.Generic.List<AvatarInfo> avatars = await players
                        .GetAvatarsForAccountAsync(accountId, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(new LoginResponse(avatars.Count == 0));
                }
            )
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("Login")
            .WithSummary("Authenticate an account and start a web session.")
            .WithTags(TagAuth);

        app.MapPost(
                "/api/public/authentication/logout",
                Ok<EmptyResponse> (HttpContext ctx, WebApiSessionStore sessions) =>
                {
                    string? sessionId = ctx.SessionId();

                    if (sessionId is not null)
                    {
                        sessions.RemoveSession(sessionId);
                    }

                    ctx.ClearSessionCookie();

                    return TypedResults.Ok(EmptyResponse.Instance);
                }
            )
            .WithName("Logout")
            .WithSummary("End the current web session.")
            .WithTags(TagAuth);
    }

    private static void MapUser(WebApplication app)
    {
        app.MapPost(
                "/api/public/registration/new",
                async Task<
                    Results<
                        Ok<RegistrationResponse>,
                        BadRequest<ApiErrorResponse>,
                        Conflict<ApiErrorResponse>
                    >
                > (
                    HttpContext ctx,
                    RegisterRequest body,
                    IWebApiAuthService auth,
                    CancellationToken ct
                ) =>
                {
                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(
                            new ApiErrorResponse("pocket.auth.missing_credentials")
                        );
                    }

                    (bool success, int accountId, string? error) = await auth.RegisterAsync(
                            body.Email!,
                            body.Password!,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (!success)
                    {
                        return TypedResults.Conflict(new ApiErrorResponse(error ?? "email_taken"));
                    }

                    (bool loginOk, string? sessionId, _, _) = await auth.LoginAsync(
                            body.Email!,
                            body.Password!,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (loginOk && sessionId is not null)
                    {
                        ctx.IssueSessionCookie(sessionId);
                    }

                    return TypedResults.Ok(new RegistrationResponse(accountId));
                }
            )
            .RequireRateLimiting(RegistrationRateLimitPolicy)
            .WithName("Register")
            .WithSummary("Create a new account and auto-start a web session.")
            .WithTags(TagAuth);

        app.MapGet(
                "/api/user/avatars",
                // Also the identity probe: the 401 in this union is what the site reads as "signed
                // out", which is why it has no dedicated route.
                async Task<
                    Results<Ok<System.Collections.Generic.List<AvatarInfo>>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    return TypedResults.Ok(
                        await players
                            .GetAvatarsForAccountAsync(accountId.Value, ct)
                            .ConfigureAwait(false)
                    );
                }
            )
            .WithName("GetAvatars")
            .WithSummary("List the avatars owned by the authenticated account.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/avatars",
                async Task<
                    Results<
                        Ok<System.Collections.Generic.List<AvatarInfo>>,
                        BadRequest<ApiErrorResponse>,
                        Conflict<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    CreateAvatarRequest body,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    (bool success, int _, string? error) = await players
                        .CreateAvatarAsync(
                            accountId.Value,
                            body.Name!,
                            body.Figure ?? string.Empty,
                            body.Gender ?? "M",
                            ct
                        )
                        .ConfigureAwait(false);

                    if (!success)
                    {
                        return TypedResults.Conflict(
                            new ApiErrorResponse(error ?? "invalid_request")
                        );
                    }

                    return TypedResults.Ok(
                        await players
                            .GetAvatarsForAccountAsync(accountId.Value, ct)
                            .ConfigureAwait(false)
                    );
                }
            )
            .WithName("CreateAvatar")
            .WithSummary("Create an avatar and return the refreshed avatar list.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/avatars/select",
                async Task<
                    Results<
                        Ok<EmptyResponse>,
                        BadRequest<ApiErrorResponse>,
                        UnauthorizedError,
                        ForbiddenError
                    >
                > (
                    HttpContext ctx,
                    SelectAvatarRequest body,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    if (!int.TryParse(body.UniqueId, out int playerId))
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_unique_id"));
                    }

                    System.Collections.Generic.List<AvatarInfo> owned = await players
                        .GetAvatarsForAccountAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    if (!owned.Exists(a => a.UniqueId == body.UniqueId))
                    {
                        return new ForbiddenError("avatar_not_owned");
                    }

                    sessions.SetSelectedPlayer(ctx.SessionId(), playerId);

                    return TypedResults.Ok(EmptyResponse.Instance);
                }
            )
            .WithName("SelectAvatar")
            .WithSummary("Select the avatar used for the next SSO token.")
            .WithTags(TagUser);

        app.MapGet(
                "/api/ssotoken",
                async Task<
                    Results<
                        Ok<SsoTicketResponse>,
                        NotFound<ApiErrorResponse>,
                        InternalServerError<ApiErrorResponse>,
                        UnauthorizedError,
                        ForbiddenError
                    >
                > (
                    HttpContext ctx,
                    string? uniqueId,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    IWebApiAuthService auth,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    int? selectedFromSession = sessions.GetSelectedPlayer(ctx.SessionId());
                    int playerId;

                    if (selectedFromSession.HasValue)
                    {
                        playerId = selectedFromSession.Value;
                    }
                    else if (
                        !string.IsNullOrWhiteSpace(uniqueId) && int.TryParse(uniqueId, out int pid)
                    )
                    {
                        System.Collections.Generic.List<AvatarInfo> ownedForSso = await players
                            .GetAvatarsForAccountAsync(accountId.Value, ct)
                            .ConfigureAwait(false);

                        if (!ownedForSso.Exists(a => a.UniqueId == uniqueId))
                        {
                            return new ForbiddenError("avatar_not_owned");
                        }

                        playerId = pid;
                    }
                    else
                    {
                        System.Collections.Generic.List<AvatarInfo> list = await players
                            .GetAvatarsForAccountAsync(accountId.Value, ct)
                            .ConfigureAwait(false);

                        if (list.Count == 0)
                        {
                            return TypedResults.NotFound(
                                new ApiErrorResponse("pocket.auth.no_avatars")
                            );
                        }

                        if (!int.TryParse(list[0].UniqueId, out playerId))
                        {
                            return TypedResults.InternalServerError(
                                new ApiErrorResponse("internal")
                            );
                        }
                    }

                    (bool success, string? ticket, string? error) = await auth.GetSsoTokenAsync(
                            playerId,
                            ctx.RemoteIp(),
                            ct
                        )
                        .ConfigureAwait(false);

                    if (!success)
                    {
                        return new ForbiddenError(error ?? "avatar_not_owned");
                    }

                    return TypedResults.Ok(new SsoTicketResponse(ticket!));
                }
            )
            .RequireRateLimiting(SsoTokenRateLimitPolicy)
            .WithName("SsoToken")
            .WithSummary("Issue a single-use SSO ticket for the selected avatar.")
            .WithTags(TagUser);
    }

    private static void MapNewUser(WebApplication app)
    {
        app.MapPost(
                "/api/newuser/name/check",
                async Task<Results<Ok<NameCheckResponse>, BadRequest<ApiErrorResponse>>> (
                    NameRequest body,
                    IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    bool available = await players
                        .NameAvailableAsync(body.Name!, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(new NameCheckResponse(body.Name!, available));
                }
            )
            .WithName("NameCheck")
            .WithSummary("Check whether a player name is available.")
            .WithTags(TagNewUser);

        app.MapPost(
                "/api/newuser/name/select",
                async Task<
                    Results<
                        Ok<NameSelectResponse>,
                        BadRequest<ApiErrorResponse>,
                        Conflict<ApiErrorResponse>,
                        UnauthorizedError,
                        ForbiddenError
                    >
                > (
                    HttpContext ctx,
                    NameSelectRequest body,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    System.Collections.Generic.List<AvatarInfo> ownedForName = await players
                        .GetAvatarsForAccountAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    if (!ownedForName.Exists(a => a.UniqueId == body.PlayerId.ToString()))
                    {
                        return new ForbiddenError("avatar_not_owned");
                    }

                    bool ok = await players
                        .SetNameAsync(body.PlayerId, body.Name!, ct)
                        .ConfigureAwait(false);

                    if (!ok)
                    {
                        return TypedResults.Conflict(
                            new ApiErrorResponse("pocket.auth.name_taken")
                        );
                    }

                    return TypedResults.Ok(new NameSelectResponse(body.Name!));
                }
            )
            .WithName("NameSelect")
            .WithSummary("Assign a name to an owned avatar.")
            .WithTags(TagNewUser);
    }

    private static UnauthorizedError Unauthorized() => new("unauthorized");
}
