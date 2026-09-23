using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Platform.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Security;
using Vortex.Observability.Configuration;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// Maps the dashboard HTTP surface onto minimal-API endpoints. Authentication is handled by
/// <see cref="DashboardAuthenticationHandler"/> and authorization by per-capability policies (named
/// after the capability string), so each endpoint just declares the capability it needs via
/// <c>RequireAuthorization</c>. Every endpoint is tagged for Swagger grouping.
/// </summary>
internal static partial class DashboardEndpoints
{
    public const string LoginRateLimitPolicy = "dashboard-login";

    /// <summary>
    /// Guards the routes that accept a second-factor code from an already-authenticated caller
    /// (<c>/mfa/enable</c>, <c>/mfa/disable</c>, <c>/mfa/step-up</c>). Login's limiter does not cover
    /// them, and unthrottled they turn a six-digit code — three of which are valid at any instant,
    /// since TotpCodes allows a step either side — into something a held session can simply grind at
    /// until the step-up gate on currency, staff roles, the console and backups opens.
    /// </summary>
    public const string MfaRateLimitPolicy = "dashboard-mfa";

    private const string TagAuth = "Auth";
    private const string TagMonitoring = "Monitoring";
    private const string TagForensics = "Forensics";
    private const string TagEconomy = "Economy";
    private const string TagDirectory = "Directory";
    private const string TagOperations = "Operations";
    private const string TagCatalog = "Catalog";
    private const string TagFurniture = "Furniture";
    private const string ApiV1 = "/api/v1";
    private const string ApiMonitoring = ApiV1 + "/monitoring";
    private const string ApiForensics = ApiV1 + "/forensics";
    private const string ApiEconomy = ApiV1 + "/economy";
    private const string ApiDirectory = ApiV1 + "/directory";
    private const string ApiOperations = ApiV1 + "/operations";
    private const string ApiCatalog = ApiV1 + "/catalog";
    private const string ApiFurniture = ApiV1 + "/furniture";
    private const string ApiMeta = ApiV1 + "/meta";

    public static void MapAuth(WebApplication app)
    {
        app.MapPost(
                "/api/login",
                async (HttpContext ctx, LoginRequest body, DashboardAuthService auth) =>
                {
                    // Mapped directly rather than through MapPost (it is anonymous and
                    // rate-limited), so DashboardRequestValidationFilter does not cover it.
                    if (
                        body is null
                        || string.IsNullOrWhiteSpace(body.Email)
                        || string.IsNullOrEmpty(body.Password)
                    )
                    {
                        return Results.BadRequest(new { error = "invalid_request" });
                    }

                    DashboardLoginResult result = await auth.LoginAsync(
                            body.Email,
                            body.Password,
                            body.Code,
                            ctx.RequestAborted
                        )
                        .ConfigureAwait(false);

                    return result.Outcome switch
                    {
                        // A code that verified is the only way past the MfaRequired branch, so its
                        // presence is the answer to "does this account have a factor" -- no re-read.
                        DashboardLoginOutcome.Authenticated => IssueSession(
                            ctx,
                            result,
                            usedMfa: !string.IsNullOrWhiteSpace(body.Code)
                        ),
                        DashboardLoginOutcome.Forbidden => Results.Json(
                            new { error = "forbidden" },
                            statusCode: StatusCodes.Status403Forbidden
                        ),
                        // Both stay 401 with no session: the credentials were right, but "right
                        // credentials" is not what opens the dashboard once a factor is enrolled.
                        DashboardLoginOutcome.MfaRequired => Results.Json(
                            new { error = "mfa_required" },
                            statusCode: StatusCodes.Status401Unauthorized
                        ),
                        DashboardLoginOutcome.InvalidCode => Results.Json(
                            new { error = "invalid_code" },
                            statusCode: StatusCodes.Status401Unauthorized
                        ),
                        _ => Results.Json(
                            new { error = "invalid_credentials" },
                            statusCode: StatusCodes.Status401Unauthorized
                        ),
                    };
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("Login")
            .WithSummary("Authenticate a dashboard operator and start a session.")
            .WithTags(TagAuth);

        app.MapPost(
                "/api/logout",
                (HttpContext ctx, DashboardAuthService auth) =>
                {
                    auth.Logout(
                        ctx.Request.Cookies[DashboardAuthenticationHandler.SessionCookieName]
                    );
                    ctx.Response.Cookies.Delete(
                        DashboardAuthenticationHandler.SessionCookieName,
                        new CookieOptions { Path = "/" }
                    );
                    return Results.Ok(new { ok = true });
                }
            )
            .AllowAnonymous()
            .WithName("Logout")
            .WithSummary("End the current dashboard session.")
            .WithTags(TagAuth);

        app.MapGet(
                "/api/me",
                async (HttpContext ctx, IAccountMfaService mfa, CancellationToken ct) =>
                {
                    DashboardPrincipal? principal = ctx.GetDashboardPrincipal();

                    if (principal is null)
                    {
                        return Results.Json(
                            new { error = "unauthenticated" },
                            statusCode: StatusCodes.Status401Unauthorized
                        );
                    }

                    bool mfaEnabled = await mfa.IsEnabledAsync(principal.AccountId, ct)
                        .ConfigureAwait(false);

                    return Results.Ok(BuildIdentity(principal, mfaEnabled));
                }
            )
            .RequireAuthorization()
            .WithName("Identity")
            .WithSummary("The authenticated operator and their effective capabilities.")
            .WithTags(TagAuth);
    }

    public static void MapReadApi(WebApplication app, Func<DateTime> startedAtUtc)
    {
        MapMonitoringReads(app, startedAtUtc);
        MapAuditReads(app);
        MapChatlogReads(app);
        MapEconomyReads(app);
        MapDirectoryReads(app);
        MapRoomReads(app);
        MapCatalogReads(app);
        MapFurnitureReads(app);
        MapStatsReads(app);
        MapTargetedOfferReads(app);
        MapQuestReads(app);
        MapHabbiconReads(app);
        MapRewardTrackReads(app);
        MapPollReads(app);
        MapQuestContentReads(app);
        MapMysteryBoxReads(app);
        MapSongReads(app);
        MapFishingReads(app);
        MapPrizePoolReads(app);
        MapConfigReads(app);
        MapBackupReads(app);
        MapAchievementReads(app);
        MapBotReads(app);
        MapGuildReads(app);
        MapNavigatorReads(app);
        MapArticleReads(app);
        MapGamedataReads(app);
        MapHotelViewReads(app);
        MapInsightReads(app);
        MapBenchmarkReads(app);
        MapBenchmarkRunReads(app);
        MapConsoleReads(app);
        MapWordFilterReads(app);
    }

    public static void MapOperations(WebApplication app)
    {
        MapCurrencyOperations(app);
        MapVoucherOperations(app);
        MapModerationOperations(app);
        MapRoomOperations(app);
        MapCatalogOperations(app);
        MapFurnitureOperations(app);
        MapTargetedOfferOperations(app);
        MapQuestOperations(app);
        MapHabbiconOperations(app);
        MapRewardTrackOperations(app);
        MapPollOperations(app);
        MapQuestContentOperations(app);
        MapMysteryBoxOperations(app);
        MapSongOperations(app);
        MapFishingOperations(app);
        MapPrizePoolOperations(app);
        MapConfigOperations(app);
        MapBackupOperations(app);
        MapBenchmarkOperations(app);
        MapNavigatorOperations(app);
        MapStaffOperations(app);
        MapGuildOperations(app);
        MapContentOperations(app);
        MapArticleOperations(app);
        MapFeedOperations(app);
        MapWordFilterOperations(app);
        MapGamedataOperations(app);
        MapHotelViewOperations(app);
        MapConsoleOperations(app);
    }

    public static void MapMeta(WebApplication app)
    {
        app.MapGet(
                ApiMeta + "/endpoints",
                (IEnumerable<EndpointDataSource> dataSources) =>
                {
                    // Non-nullable: the nulls are filtered out by the OfType below. The serialized
                    // response shape is unchanged.
                    ApiRouteDescriptor[] routes = dataSources
                        .SelectMany(source => source.Endpoints.OfType<RouteEndpoint>())
                        .Select(endpoint =>
                        {
                            string? route = endpoint.RoutePattern.RawText;

                            if (string.IsNullOrWhiteSpace(route) || !route.StartsWith("/api/"))
                            {
                                return (ApiRouteDescriptor?)null;
                            }

                            string domain = ResolveApiDomain(route);

                            string[] methods = endpoint
                                .Metadata.OfType<IHttpMethodMetadata>()
                                .SelectMany(m => m.HttpMethods)
                                .Distinct()
                                .OrderBy(m => m)
                                .ToArray();

                            if (methods.Length == 0)
                            {
                                methods = ["GET"];
                            }

                            // OfType<string>() drops null policies *and* yields a non-nullable element
                            // type, matching the ApiRouteDescriptor.Capabilities: string[] parameter.
                            string[] capabilities = endpoint
                                .Metadata.OfType<IAuthorizeData>()
                                .Select(auth => auth.Policy)
                                .OfType<string>()
                                .Where(policy => !string.IsNullOrWhiteSpace(policy))
                                .Distinct()
                                .OrderBy(policy => policy)
                                .ToArray();

                            string[] tags = endpoint
                                .Metadata.OfType<ITagsMetadata>()
                                .SelectMany(tag => tag.Tags)
                                .Distinct()
                                .OrderBy(tag => tag)
                                .ToArray();

                            return new ApiRouteDescriptor(
                                domain,
                                route,
                                methods,
                                tags,
                                capabilities,
                                capabilities.Length > 0,
                                endpoint.DisplayName
                            );
                        })
                        // OfType drops the nulls *and* yields a non-nullable element type, so the
                        // aggregations below stop dereferencing a `T?` (CS8602). Behaviour is
                        // identical to the previous `Where(route => route is not null)`.
                        .OfType<ApiRouteDescriptor>()
                        .OrderBy(route => route.Domain)
                        .ThenBy(route => route.Path)
                        .ToArray();

                    ApiDomainGroup[] groups = routes
                        .GroupBy(route => route.Domain)
                        .OrderBy(group => group.Key)
                        .Select(group => new ApiDomainGroup(
                            group.Key,
                            group.Count(),
                            [
                                .. group
                                    .SelectMany(route => route.Methods)
                                    .Distinct()
                                    .OrderBy(method => method),
                            ]
                        ))
                        .ToArray();

                    ApiMethodUsage[] methodUsage = routes
                        .SelectMany(route => route.Methods)
                        .GroupBy(method => method)
                        .OrderBy(group => group.Key)
                        .Select(group => new ApiMethodUsage(group.Key, group.Count()))
                        .ToArray();

                    return Results.Ok(
                        new ApiRouteCatalog("1", DateTime.UtcNow, routes, groups, methodUsage)
                    );
                }
            )
            .Produces<ApiRouteCatalog>()
            .RequireAuthorization(Capabilities.Dashboard.OverviewRead)
            .WithTags(TagMonitoring)
            .WithSummary("List dashboard API routes with methods and capability requirements.")
            .WithName("DashboardApiRouteCatalog");
    }

    /// <summary>Serves the bundled SPA shell and hashed assets. Registered only when the front-end is enabled.</summary>
    public static void MapFrontend(WebApplication app)
    {
        app.MapGet(
                "/assets/{file}",
                (string file, DashboardAssetStore store) =>
                    store.TryGetAsset(file, out byte[] bytes, out string contentType)
                        ? Results.Bytes(bytes, contentType)
                        : Results.NotFound()
            )
            .AllowAnonymous()
            .ExcludeFromDescription();

        MapHotelAssets(app);

        // SPA client-side routing: any non-API navigation returns the shell; the client gates views
        // from /api/me. Unknown /api routes still 404 as JSON.
        app.MapFallback(
                (HttpContext ctx, DashboardAssetStore store) =>
                    (ctx.Request.Path.Value ?? "/").StartsWith(
                        "/api/",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? Results.Json(
                            new { error = "not_found" },
                            statusCode: StatusCodes.Status404NotFound
                        )
                        : Results.Bytes(store.GetHtmlBytes("index.html"), store.HtmlContentType)
            )
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    private static IResult IssueSession(HttpContext ctx, DashboardLoginResult result, bool usedMfa)
    {
        DashboardSessionStore sessions =
            ctx.RequestServices.GetRequiredService<DashboardSessionStore>();

        // Unconditional, not ctx.Request.IsHttps: behind the TLS-terminating proxy this listener is
        // meant to sit behind, Kestrel sees plain http and the scheme test would drop Secure from the
        // operator's session cookie. The only opt-out is the explicit cleartext opt-in the operator
        // already had to set to bind this listener off-box at all. See the same reasoning in
        // WebApiHttpContextExtensions.IssueSessionCookie.
        bool allowInsecure = ctx
            .RequestServices.GetRequiredService<IOptions<ObservabilityConfig>>()
            .Value.DashboardAllowInsecureRemoteHttp;

        ctx.Response.Cookies.Append(
            DashboardAuthenticationHandler.SessionCookieName,
            result.SessionId!,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !allowInsecure,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                IsEssential = true,
                MaxAge = TimeSpan.FromSeconds(sessions.LifetimeSeconds),
            }
        );

        return Results.Ok(BuildIdentity(result.Principal!, mfaEnabled: usedMfa));
    }

    private static object BuildIdentity(DashboardPrincipal principal, bool mfaEnabled) =>
        new
        {
            email = principal.Email,
            superuser = principal.Permissions.IsSuperUser,
            capabilities = principal.Permissions.Capabilities,
            mfaEnabled,
        };

    // VSTHRD003: both helpers deliberately await a task handed in by the endpoint delegate (already
    // started by the read call at the call site) purely to wrap its result/null-check
    // in a Results.Ok/Json — there is no deadlock risk since nothing here owns or blocks on the task.
#pragma warning disable VSTHRD003
    private static async Task<IResult> OkAsync<TResponse>(Task<TResponse> task) =>
        Results.Ok(await task.ConfigureAwait(false));

    private static async Task<IResult> OkNullableAsync<TResponse>(Task<TResponse?> task)
        where TResponse : class
    {
        TResponse? payload = await task.ConfigureAwait(false);
        return payload is null
            ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(payload);
    }
#pragma warning restore VSTHRD003

    /// <summary>A permanent sanction needs no duration; a temporary one needs a positive duration.</summary>
    private static bool HasValidDuration(bool permanent, int? durationSeconds) =>
        permanent || durationSeconds is > 0;

    private static void MapReadGet(
        WebApplication app,
        string path,
        Delegate handler,
        string capability,
        string tag
    ) => app.MapGet(path, handler).RequireAuthorization(capability).WithTags(tag);

    /// <summary>
    /// A read whose response shape is declared, so OpenAPI describes it and the front end can derive
    /// its types from the document instead of being told the shape twice.
    /// </summary>
    /// <remarks>
    /// The handler returns <see cref="IResult" />, which carries no type information, so the schema
    /// has to be stated here. That is the whole reason this overload exists: without it the document
    /// says a route answers 200 and nothing about with what, and a generator reads that as
    /// <c>unknown</c>. A subject earns it by giving its reads real return types rather than
    /// <c>object</c>.
    /// </remarks>
    private static void MapReadGet<TResponse>(
        WebApplication app,
        string path,
        Delegate handler,
        string capability,
        string tag
    ) =>
        app.MapGet(path, handler)
            .RequireAuthorization(capability)
            .WithTags(tag)
            .Produces<TResponse>();

    /// <summary>A read that answers 404 when the thing asked for is not there.</summary>
    private static void MapReadGetNullable<TResponse>(
        WebApplication app,
        string path,
        Delegate handler,
        string capability,
        string tag
    ) =>
        app.MapGet(path, handler)
            .RequireAuthorization(capability)
            .WithTags(tag)
            .Produces<TResponse>()
            .Produces(StatusCodes.Status404NotFound);

    /// <summary>
    /// Every dashboard write goes through here, so the two checks that used to open each endpoint's
    /// own validation block — a body arrived, and it carries a usable reason — are applied once by
    /// <see cref="DashboardRequestValidationFilter"/> instead of being remembered 47 times. Endpoints
    /// keep their own field rules.
    /// </summary>
    /// <remarks>
    /// It also decides, from the capability alone, whether the route needs a recent second factor —
    /// see <see cref="StepUpRequired.Capabilities"/>. Deciding it here rather than per route is what
    /// makes the rule hold for endpoints nobody has written yet: a new route that grants currency
    /// carries the requirement because of what it is allowed to do, not because its author
    /// remembered.
    /// </remarks>
    private static void MapPost(
        WebApplication app,
        string path,
        Delegate handler,
        string capability,
        string tag
    )
    {
        RouteHandlerBuilder route = app.MapPost(path, handler)
            .RequireAuthorization(capability)
            .WithTags(tag)
            .AddEndpointFilter<DashboardRequestValidationFilter>();

        if (StepUpRequired.Capabilities.Contains(capability))
        {
            route.WithMetadata(StepUpRequired.Instance).AddEndpointFilter<DashboardStepUpFilter>();
        }
    }

    private static string ResolveApiDomain(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "misc";
        }

        string normalized = route.Trim('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (
            parts.Length >= 3
            && parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("v1", StringComparison.OrdinalIgnoreCase)
        )
        {
            return parts[2];
        }

        return "misc";
    }
}
