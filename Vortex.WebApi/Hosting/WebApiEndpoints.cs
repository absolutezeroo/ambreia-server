using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Primitives.Hosting;
using Vortex.WebApi.Http.Errors;
using Vortex.WebApi.Http.Requests;
using Vortex.WebApi.Http.Responses;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// Maps the client-facing web API onto minimal-API endpoints — one handler per route. The web API
/// signs an account in and hands it a ticket; the characters are chosen on the game socket (spec F3a
/// §5.1), never here. Authentication is a cookie-backed session (see
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

    public static void Map(WebApplication app)
    {
        MapPublic(app);
        MapAuthentication(app);
        MapUser(app);
        MapEditor(app);
        MapContent(app);
        MapCharacters(app);
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

        // Liveness: is the process answering? Always 200, so an orchestrator restarts the hotel for
        // a dead process and nothing else — a database away or a start still in progress is not
        // cured by a restart. This is what the container HEALTHCHECK probes.
        app.MapGet(
                "/health",
                Ok<HealthResponse> (RequiredServiceGuard guard) =>
                    TypedResults.Ok(
                        new HealthResponse(
                            guard.IsDegraded ? "Degraded" : "Healthy",
                            guard.DegradedServices
                        )
                    )
            )
            .WithName("Health")
            .WithSummary(
                "Liveness probe: 200 while the process answers; Degraded when an optional service "
                    + "failed to start (OPS-02). Never asks the database — see /ready."
            )
            .WithTags(TagPublic);

        // Readiness: can a player be routed here? Listeners open, host not draining, database
        // reachable. This is what a proxy or load balancer probes before sending traffic.
        app.MapGet(
                "/ready",
                async (
                    IHostReadiness readiness,
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
                        // A probe failure is a readiness signal, not an error to propagate - report
                        // it as part of the response body instead of a 500.
                        databaseUp = false;
                    }

                    bool ready = readiness.IsReady && databaseUp;

                    ReadinessResponse body = new(
                        ready ? "Ready" : "NotReady",
                        readiness.Blocker ?? (databaseUp ? null : "database"),
                        databaseUp ? "up" : "down",
                        guard.DegradedServices
                    );

                    // The one endpoint left on Results.Json, and the one status that cannot be
                    // typed: there is no `ServiceUnavailable<T>`, so 503-with-a-body has to be built
                    // at runtime. Both answers are declared below instead — the probe is an
                    // operations surface no generated client reads, so a declaration is the right
                    // amount of truth for it.
                    return Results.Json(
                        body,
                        statusCode: ready
                            ? StatusCodes.Status200OK
                            : StatusCodes.Status503ServiceUnavailable
                    );
                }
            )
            .WithName("Ready")
            .WithSummary(
                "Readiness probe: 200 when the listeners are open, the host is not draining and the "
                    + "database answers; 503 with the blocker otherwise."
            )
            .WithTags(TagPublic)
            .Produces<ReadinessResponse>()
            .Produces<ReadinessResponse>(StatusCodes.Status503ServiceUnavailable);
    }

    private static void MapAuthentication(WebApplication app)
    {
        app.MapPost(
                "/api/public/authentication/login",
                async Task<
                    Results<Ok<EmptyResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    LoginRequest body,
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

                    (bool success, string? sessionId, _, string? error) = await auth.LoginAsync(
                            body.Email!,
                            body.Password!,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (!success)
                    {
                        return new UnauthorizedError(error ?? "pocket.auth.invalid_login");
                    }

                    ctx.IssueSessionCookie(sessionId!);

                    return TypedResults.Ok(EmptyResponse.Instance);
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
                "/api/ssotoken",
                async Task<Results<Ok<SsoTicketResponse>, UnauthorizedError>> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IWebApiAuthService auth,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    string ticket = await auth.GetAccountSsoTokenAsync(
                            accountId.Value,
                            ctx.RemoteIp(),
                            ct
                        )
                        .ConfigureAwait(false);

                    return TypedResults.Ok(new SsoTicketResponse(ticket));
                }
            )
            .RequireRateLimiting(SsoTokenRateLimitPolicy)
            .WithName("SsoToken")
            .WithSummary(
                "Issue a short-lived account ticket: the game socket opens on the character screen."
            )
            .WithTags(TagUser);
    }

    private static UnauthorizedError Unauthorized() => new("unauthorized");
}
