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
using Vortex.Primitives.Shop;
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
    public const string ReportRateLimitPolicy = "webapi-report";
    public const string ShopOrderRateLimitPolicy = "webapi-shop-order";
    public const string ChainRpcRateLimitPolicy = "webapi-chain-rpc";
    public const string FeedPostRateLimitPolicy = "webapi-feed-post";

    private const string TagPublic = "Public";
    private const string TagAuth = "Authentication";
    private const string TagUser = "User";
    private const string TagNewUser = "NewUser";
    private const string TagContent = "Content";
    private const string TagShop = "Shop";
    private const string TagChain = "Chain";

    public static void Map(WebApplication app)
    {
        MapPublic(app);
        MapProfiles(app);
        MapAuthentication(app);
        MapUser(app);
        MapTwoFactor(app);
        MapEmail(app);
        MapSafetyLock(app);
        MapPreferences(app);
        MapNewUser(app);
        MapContent(app);
        MapShop(app);

        // Where a self-custody wallet is proved and linked. Not in the game client, which has no
        // interface for it — see WebApiEndpoints.Wallet.cs.
        MapWallet(app);

        // The only route to the chain node, whose own port is deliberately unpublished — see
        // WebApiEndpoints.Chain.cs.
        WebApiEndpointsChain.MapChain(app, TagChain, ChainRpcRateLimitPolicy);

        // What VortexCollectible's tokenURI points at. Tagged Public rather than Chain because it
        // answers with or without a chain: it describes a Relic, and a Relic exists in the hotel
        // whether or not anybody ever moved it.
        WebApiEndpointsCollectibles.MapCollectibles(app, TagPublic);

        // The coin contest. Public for the same reason the chain routes are: every balance in it is
        // readable off the chain by anyone who has the addresses, and the addresses are public.
        WebApiEndpointsLeaderboard.MapLeaderboard(app, TagPublic);

        WebApiEndpointsCamera.MapCamera(app);

        // The website's social feed. The reads are anonymous, like the photo gallery beside it; the
        // publish route is the composer's, tagged User like every other route behind a session — see
        // WebApiEndpoints.Feed.cs.
        WebApiEndpointsFeed.MapFeed(app, TagPublic, TagUser);
    }

    /// <summary>
    /// Public profiles, on habbo.com's own two routes: a name lookup that answers the header, and
    /// the full read keyed by the id that lookup returns. Anonymous, because a profile is a page a
    /// signed-out visitor opens — the site links to one from every friend head.
    /// </summary>
    private static void MapProfiles(WebApplication app)
    {
        app.MapGet(
                "/api/public/users",
                async Task<Results<Ok<ProfileUser>, NotFound<ApiErrorResponse>>> (
                    string? name,
                    IWebApiProfileService profiles,
                    CancellationToken ct
                ) =>
                {
                    ProfileUser? user = await profiles
                        .FindUserByNameAsync(name ?? string.Empty, ct)
                        .ConfigureAwait(false);

                    return user is null
                        ? TypedResults.NotFound(new ApiErrorResponse("user_not_found"))
                        : TypedResults.Ok(user);
                }
            )
            .WithName("UserByName")
            .WithSummary("Resolve a habbo name to its public profile header.")
            .WithTags(TagPublic);

        app.MapGet(
                "/api/public/users/{uniqueId}/profile",
                async Task<Results<Ok<PlayerProfile>, NotFound<ApiErrorResponse>>> (
                    string uniqueId,
                    IWebApiProfileService profiles,
                    CancellationToken ct
                ) =>
                {
                    // The id is a player id rendered as a string, which is what the lookup above
                    // hands out. Parsing here rather than binding an int keeps a junk id a 404 — the
                    // same answer as an id that does not exist — instead of a 400 that tells a
                    // caller they at least guessed the shape right.
                    if (!int.TryParse(uniqueId, out int playerId))
                    {
                        return TypedResults.NotFound(new ApiErrorResponse("user_not_found"));
                    }

                    PlayerProfile? profile = await profiles
                        .GetProfileAsync(playerId, ct)
                        .ConfigureAwait(false);

                    return profile is null
                        ? TypedResults.NotFound(new ApiErrorResponse("user_not_found"))
                        : TypedResults.Ok(profile);
                }
            )
            .WithName("UserProfile")
            .WithSummary("A player's badges, friends, rooms and groups.")
            .WithTags(TagPublic);

        // habbo.com's own route for one appart ("/public/rooms/:id"). The gallery beside it is an
        // ADDITION: habbo.com's is server-rendered and calls nothing, but this site is a SPA and
        // needs a list from somewhere.
        app.MapGet(
                "/api/public/rooms",
                async Task<Ok<RoomPage>> (
                    int? page,
                    int? pageSize,
                    IWebApiRoomService rooms,
                    CancellationToken ct
                ) =>
                    TypedResults.Ok(
                        await rooms
                            .GetRoomsAsync(page ?? 1, pageSize ?? 0, ct)
                            .ConfigureAwait(false)
                    )
            )
            .WithName("Rooms")
            .WithSummary("The appart gallery: the busiest visible rooms first.")
            .WithTags(TagPublic);

        app.MapGet(
                "/api/public/rooms/{id:int}",
                async Task<Results<Ok<RoomSummary>, NotFound<ApiErrorResponse>>> (
                    int id,
                    IWebApiRoomService rooms,
                    CancellationToken ct
                ) =>
                {
                    RoomSummary? room = await rooms.GetRoomAsync(id, ct).ConfigureAwait(false);

                    return room is null
                        ? TypedResults.NotFound(new ApiErrorResponse("room_not_found"))
                        : TypedResults.Ok(room);
                }
            )
            .WithName("Room")
            .WithSummary("One appart.")
            .WithTags(TagPublic);

        // The photo gallery, and one player's photos. habbo.com serves both out of its own user
        // content service; this hotel already HAD the photos — `camera_photos` has been filled by
        // the in-game camera since it shipped, and `WebApiEndpointsCamera` has been serving the
        // blobs — and nothing had ever listed them, so the site's Photos page rendered an empty
        // state and a profile had no photo section at all.
        //
        // Published photos only. See IWebApiPhotoService for why that condition lives in one place.
        app.MapGet(
                "/api/public/photos",
                async Task<Ok<PhotoPage>> (
                    HttpContext ctx,
                    int? page,
                    int? pageSize,
                    // [FromServices] rather than left to inference, the same defence
                    // WebApiEndpointsCamera spells out: a service the container does not know is
                    // inferred as a request BODY, a GET may not have one, and the whole route table
                    // then fails to build — so one missing registration takes down every endpoint in
                    // the host rather than this one. Which is exactly what it did here first.
                    [FromServices] IWebApiPhotoService photos,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                    // The session is READ but not required: the gallery is anonymous, and the only
                    // thing it decides is whether each card comes back with `likedByMe` set.
                    TypedResults.Ok(
                        await photos
                            .GetPhotosAsync(
                                page ?? 1,
                                pageSize ?? 0,
                                (
                                    await SelectedPlayerAsync(ctx, sessions, players, ct)
                                        .ConfigureAwait(false)
                                ).PlayerId,
                                ct
                            )
                            .ConfigureAwait(false)
                    )
            )
            .WithName("Photos")
            .WithSummary("The photo gallery: every published photo, newest first.")
            .WithTags(TagPublic);

        app.MapGet(
                "/api/public/photos/{key}",
                async Task<Results<Ok<PhotoSummary>, NotFound<ApiErrorResponse>>> (
                    HttpContext ctx,
                    string key,
                    [FromServices] IWebApiPhotoService photos,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? viewerId, _) = await SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    PhotoSummary? photo = await photos
                        .GetPhotoAsync(key, viewerId, ct)
                        .ConfigureAwait(false);

                    return photo is null
                        ? TypedResults.NotFound(new ApiErrorResponse("photo_not_found"))
                        : TypedResults.Ok(photo);
                }
            )
            .WithName("Photo")
            .WithSummary("One published photo.")
            .WithTags(TagPublic);

        // Liking. A WRITE, so it is the one photo route that needs a session — and it takes the
        // wanted state rather than toggling, because a toggle sent twice by a flaky connection
        // lands on the opposite of what the player pressed.
        app.MapPut(
                "/api/user/photos/{key}/like",
                async Task<
                    Results<Ok<PhotoSummary>, NotFound<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    string key,
                    PhotoLikeRequest body,
                    [FromServices] IWebApiPhotoService photos,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        // No session, or a session with no avatar: a like belongs to a PLAYER, and
                        // an account without one has nobody to attribute it to.
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    PhotoSummary? photo = await photos
                        .SetLikeAsync(key, playerId.Value, body.Liked, ct)
                        .ConfigureAwait(false);

                    return photo is null
                        ? TypedResults.NotFound(new ApiErrorResponse("photo_not_found"))
                        : TypedResults.Ok(photo);
                }
            )
            .WithName("PhotoLike")
            .WithSummary("Like or unlike a published photo.")
            .WithTags(TagUser);

        app.MapGet(
                "/api/public/users/{uniqueId}/photos",
                async Task<Ok<IReadOnlyList<PhotoSummary>>> (
                    HttpContext ctx,
                    string uniqueId,
                    int? limit,
                    [FromServices] IWebApiPhotoService photos,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    // An unparseable id answers an EMPTY LIST, not a 404 — the same answer as a
                    // player with no photos. The profile route beside it can afford to 404 because
                    // the page has nothing to draw without a profile; a photo section is decoration
                    // on a page that already rendered, and a 404 here would turn it into a way to
                    // test whether an id exists.
                    if (!int.TryParse(uniqueId, out int playerId))
                    {
                        return TypedResults.Ok<IReadOnlyList<PhotoSummary>>([]);
                    }

                    (int? viewerId, _) = await SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(
                        await photos
                            .GetPlayerPhotosAsync(playerId, limit ?? 0, viewerId, ct)
                            .ConfigureAwait(false)
                    );
                }
            )
            .WithName("PlayerPhotos")
            .WithSummary("One player's published photos, newest first.")
            .WithTags(TagPublic);

        // Mine, for attaching, is not this player's public gallery above: PlayerPhotos requires the
        // owner's ProfileVisible, which defaults to FALSE in this hotel, so a default player asking
        // the public route for their own photos gets an empty list. The feed composer's picker asks
        // a different question — the caller's own published photos, with no ProfileVisible gate,
        // because publishing already IS the consent (the same rule WebApiFeedService.Readable()
        // states for the feed).
        app.MapGet(
                "/api/user/photos",
                async Task<
                    Results<
                        Ok<IReadOnlyList<PhotoSummary>>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    [FromServices] IWebApiPhotoService photos,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        // No session, or a session with no avatar: a photo belongs to a PLAYER, and
                        // an account without one has nobody's photos to answer.
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    return TypedResults.Ok(
                        await photos.GetMyPhotosAsync(playerId.Value, ct).ConfigureAwait(false)
                    );
                }
            )
            .WithName("MyPhotos")
            .WithSummary(
                "The signed-in player's own published photos, with no ProfileVisible gate."
            )
            .WithTags(TagUser);
    }

    /// <summary>
    /// The website's editorial reads — the news feed, an article, and the languages the hotel
    /// publishes in. Anonymous by design: this is what a visitor sees before signing in, and the
    /// front page must render with no session at all.
    /// </summary>
    private static void MapContent(WebApplication app)
    {
        app.MapGet(
                "/api/public/languages",
                async Task<Ok<SiteLanguages>> (
                    IWebApiArticleService articles,
                    CancellationToken ct
                ) => TypedResults.Ok(await articles.GetLanguagesAsync(ct).ConfigureAwait(false))
            )
            .WithName("Languages")
            .WithSummary("The enabled site languages and which one is the fallback.")
            .WithTags(TagContent);

        app.MapGet(
                "/api/public/articles",
                async Task<Ok<ArticleFeed>> (
                    HttpContext ctx,
                    IWebApiArticleService articles,
                    CancellationToken ct,
                    string? category = null,
                    string? lang = null,
                    int page = 1,
                    int pageSize = 0
                ) =>
                    TypedResults.Ok(
                        await articles
                            .GetFeedAsync(
                                category,
                                lang ?? ctx.AcceptedLanguages(),
                                page,
                                pageSize,
                                ct
                            )
                            .ConfigureAwait(false)
                    )
            )
            .WithName("Articles")
            .WithSummary("A page of the news feed, newest first, pinned articles ahead of it.")
            .WithTags(TagContent);

        app.MapGet(
                "/api/public/articles/{slug}",
                async Task<Results<Ok<ArticleDetail>, NotFound<ApiErrorResponse>>> (
                    HttpContext ctx,
                    string slug,
                    IWebApiArticleService articles,
                    CancellationToken ct,
                    string? lang = null
                ) =>
                {
                    ArticleDetail? article = await articles
                        .GetArticleAsync(slug, lang ?? ctx.AcceptedLanguages(), ct)
                        .ConfigureAwait(false);

                    return article is null
                        ? TypedResults.NotFound(new ApiErrorResponse("article_not_found"))
                        : TypedResults.Ok(article);
                }
            )
            .WithName("Article")
            .WithSummary("One article with its body blocks and its read-also list.")
            .WithTags(TagContent);
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
                        await auth.LoginAsync(
                                body.Email!,
                                body.Password!,
                                body.Code,
                                ctx.RemoteIp(),
                                ctx.UserAgent(),
                                ct
                            )
                            .ConfigureAwait(false);

                    if (!success)
                    {
                        // mfa_required is not a failure the visitor can do anything about except
                        // send a code, so it rides the same 401 as the rest: the client tells them
                        // apart by the error string, and neither ever carries a session. That string
                        // is why the 401 needs a body at all — see UnauthorizedError.
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
                "/api/public/authentication/password",
                async Task<
                    Results<
                        Ok<PasswordChangeResponse>,
                        BadRequest<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    ChangePasswordRequest body,
                    WebApiSessionStore sessions,
                    IAccountPasswordService passwords,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return new UnauthorizedError("pocket.auth.not_authenticated");
                    }

                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(
                            new ApiErrorResponse("pocket.auth.missing_credentials")
                        );
                    }

                    PasswordChangeResult result = await passwords
                        .ChangeAsync(
                            accountId.Value,
                            body.CurrentPassword!,
                            body.NewPassword!,
                            body.Code,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (!result.Succeeded)
                    {
                        return TypedResults.BadRequest(
                            new ApiErrorResponse(
                                result.Outcome switch
                                {
                                    PasswordChangeOutcome.MfaRequired => "pocket.auth.mfa_required",
                                    PasswordChangeOutcome.InvalidCode => "pocket.auth.invalid_code",
                                    PasswordChangeOutcome.TooShort =>
                                        "pocket.auth.password_too_short",
                                    _ => "pocket.auth.wrong_password",
                                }
                            )
                        );
                    }

                    // Every session of the account is gone, this one included. Clearing the cookie
                    // is what stops the browser presenting a token that no longer resolves.
                    ctx.ClearSessionCookie();

                    return TypedResults.Ok(new PasswordChangeResponse(result.SessionsRevoked));
                }
            )
            .WithName("ChangePassword")
            .WithSummary("Change the signed-in account's password and end every session it has.")
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

                    // An account created a moment ago cannot have a second factor yet, nor security
                    // questions, so there is no code to pass on and the place cannot be anything but
                    // trusted. Both are passed anyway rather than defaulted: the day registration
                    // grows a step that could change either, this call has already been told.
                    (bool loginOk, string? sessionId, _, _) = await auth.LoginAsync(
                            body.Email!,
                            body.Password!,
                            code: null,
                            ctx.RemoteIp(),
                            ctx.UserAgent(),
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
                "/api/user/purse",
                async Task<
                    Results<Ok<PlayerPurse>, NotFound<ApiErrorResponse>, UnauthorizedError>
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

                    // The selected avatar when there is one, and otherwise the account's first —
                    // the same fallback the SSO ticket route makes, because a session that has not
                    // been through the avatar picker still has a wallet to show.
                    int? playerId = sessions.GetSelectedPlayer(ctx.SessionId());

                    if (playerId is null)
                    {
                        System.Collections.Generic.List<AvatarInfo> owned = await players
                            .GetAvatarsForAccountAsync(accountId.Value, ct)
                            .ConfigureAwait(false);

                        if (owned.Count == 0)
                        {
                            return TypedResults.NotFound(
                                new ApiErrorResponse("pocket.auth.no_avatars")
                            );
                        }

                        playerId = int.Parse(owned[0].UniqueId, CultureInfo.InvariantCulture);
                    }

                    PlayerPurse? purse = await players
                        .GetPurseAsync(playerId.Value, ct)
                        .ConfigureAwait(false);

                    return purse is null
                        ? TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"))
                        : TypedResults.Ok(purse);
                }
            )
            .WithName("GetPurse")
            .WithSummary("The selected avatar's credits, diamonds, duckets and subscriptions.")
            .WithTags(TagUser);

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

                    // habbo.com's own gate, and the only place it puts one:
                    //   avatar-create.html's open() is
                    //   `Session.isTrusted() ? create() : safetyLockModal.open().then(create)`
                    // A new avatar is the risky action a thief would reach for first — it is a new
                    // identity on the hotel, wearing the owner's account.
                    if (!sessions.IsTrusted(ctx.SessionId()))
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("safetylock.required"));
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

        // What the player saw and the server did not. Every other observability surface here
        // records something the hotel noticed itself: the error grouping needs an exception, the
        // metrics need a counter, the audit trail needs an action somebody invoked. A room that
        // renders blank throws nothing, increments nothing and invokes nothing — it is only a bug
        // because a person says so, and this is the route that lets them.
        //
        // It writes an audit record rather than opening a table of its own. An audit event already
        // is "who, where, under which correlation id, with what payload", the investigation UI
        // already reads it, and a second store would be a second thing to back up, page and
        // retain. The category is what keeps the reports findable — see AuditCategory.PlayerReport.
        //
        // Authenticated, deliberately. An anonymous report cannot be replied to, cannot be
        // correlated with what that account was doing, and is a spam surface the rate limit alone
        // would have to hold shut.
        app.MapPost(
                "/api/user/reports",
                Results<Accepted<EmptyResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError> (
                    HttpContext ctx,
                    SubmitReportRequest body,
                    WebApiSessionStore sessions,
                    IAuditSink audit
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

                    int? playerId = sessions.GetSelectedPlayer(ctx.SessionId());

                    audit.Emit(
                        new AuditEvent
                        {
                            Category = AuditCategory.PlayerReport,
                            Action = "player.bug_report",
                            Severity = AuditSeverity.Notice,
                            Result = AuditResult.Success,
                            ActorPlayerId = playerId,
                            RoomId = body.RoomId,

                            // Null for the same reason DashboardAuditEmitter leaves it null: the
                            // IP hashing secret lives behind the authentication module, and a
                            // second hashing scheme here would produce hashes that match nothing
                            // in the rest of the trail.
                            IpHash = null,
                            Data = JsonSerializer.Serialize(
                                new
                                {
                                    accountId = accountId.Value,
                                    message = body.Message,
                                    page = body.Page,
                                    clientVersion = body.ClientVersion,
                                    console = body.Console,
                                }
                            ),
                        }
                    );

                    // 202, not 200: Emit is a non-blocking enqueue, so the row is not written yet
                    // and claiming otherwise would be a lie the client could catch.
                    return TypedResults.Accepted((string?)null, EmptyResponse.Instance);
                }
            )
            .RequireRateLimiting(ReportRateLimitPolicy)
            .WithName("SubmitReport")
            .WithSummary("File a player bug report against the current session.")
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

        app.MapPost(
                "/api/user/look/save",
                async Task<
                    Results<
                        Ok<EmptyResponse>,
                        NotFound<ApiErrorResponse>,
                        BadRequest<ApiErrorResponse>,
                        UnauthorizedError,
                        ForbiddenError
                    >
                > (
                    HttpContext ctx,
                    SaveFigureRequest body,
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

                    System.Collections.Generic.List<AvatarInfo> ownedForFigure = await players
                        .GetAvatarsForAccountAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    if (!ownedForFigure.Exists(a => a.UniqueId == body.PlayerId.ToString()))
                    {
                        return new ForbiddenError("avatar_not_owned");
                    }

                    bool ok = await players
                        .SaveFigureAsync(body.PlayerId, body.FigureString!, body.Gender ?? "M", ct)
                        .ConfigureAwait(false);

                    // The 404 used to carry `{}` like the success did — the same empty body at two
                    // statuses, which told a caller nothing. It says which avatar was not found now.
                    return ok
                        ? TypedResults.Ok(EmptyResponse.Instance)
                        : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                }
            )
            .WithName("SaveFigure")
            .WithSummary("Persist the figure string for an owned avatar.")
            .WithTags(TagUser);
    }

    /// <summary>
    /// The account safety lock, and the security questions that arm and lift it.
    ///
    /// <para>
    /// The lock is the older of the two and keeps its own routes: the owner can throw it by hand
    /// with their password, which habbo.com has no button for and which is the right thing to reach
    /// for the moment you believe someone else is in your account. The questions below are
    /// habbo.com's mechanism on top — a sign-in from a place the account has never answered from
    /// arms the same lock, and answering lifts it.
    /// </para>
    /// </summary>
    private static void MapSafetyLock(WebApplication app)
    {
        app.MapGet(
                "/api/user/safetylock",
                async Task<Results<Ok<SafetyLockResponse>, UnauthorizedError>> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IAccountSafetyLockService locks,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    bool? locked = await locks
                        .IsLockedAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(new SafetyLockResponse(locked ?? false));
                }
            )
            .WithName("GetSafetyLock")
            .WithSummary("Whether the account's safety lock is on.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/safetylock",
                async Task<
                    Results<Ok<SafetyLockResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    SafetyLockRequest body,
                    WebApiSessionStore sessions,
                    IAccountSafetyLockService locks,
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
                        return TypedResults.BadRequest(
                            new ApiErrorResponse("pocket.auth.missing_credentials")
                        );
                    }

                    SafetyLockResult result = await locks
                        .SetAsync(
                            accountId.Value,
                            body.Locked!.Value,
                            body.CurrentPassword!,
                            body.Code,
                            ct
                        )
                        .ConfigureAwait(false);

                    return result.Succeeded
                        ? TypedResults.Ok(new SafetyLockResponse(body.Locked!.Value))
                        : TypedResults.BadRequest(
                            new ApiErrorResponse(
                                result.Outcome switch
                                {
                                    SafetyLockOutcome.MfaRequired => "pocket.auth.mfa_required",
                                    SafetyLockOutcome.InvalidCode => "pocket.auth.invalid_code",
                                    _ => "pocket.auth.wrong_password",
                                }
                            )
                        );
                }
            )
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("SetSafetyLock")
            .WithSummary("Throw or lift the account's safety lock, against the current password.")
            .WithTags(TagUser);

        MapSafetyQuestions(app);
    }

    /// <summary>
    /// habbo.com's "Protection du compte": the two security questions that arm the safety lock, the
    /// challenge that lifts it, and the trusted locations that stop it being put again.
    /// </summary>
    /// <remarks>
    /// The four routes split along exactly one line — what they demand before they will act. Save
    /// and disable are changes to the recovery path and demand the PASSWORD; unlock is the challenge
    /// itself and demands the ANSWERS, because a visitor who could produce the password would not be
    /// standing in front of it. Reset demands a session that has already cleared the challenge,
    /// which is the same thing one step removed.
    /// </remarks>
    private static void MapSafetyQuestions(WebApplication app)
    {
        app.MapGet(
                "/api/user/safetyquestions",
                async Task<Results<Ok<SafetyQuestionsResponse>, UnauthorizedError>> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IAccountSafetyQuestionsService questions,
                    IAccountSafetyLockService locks,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    SafetyQuestionsStatus status = await questions
                        .GetStatusAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    bool? locked = await locks
                        .IsLockedAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(
                        new SafetyQuestionsResponse(
                            status.Configured,
                            status.Question1,
                            status.Question2,
                            sessions.IsTrusted(ctx.SessionId()),
                            locked ?? false
                        )
                    );
                }
            )
            .WithName("GetSafetyQuestions")
            .WithSummary("The account's security questions, and whether this session has answered.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/safetyquestions",
                async Task<
                    Results<Ok<EmptyResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    SafetyQuestionsSaveRequest body,
                    WebApiSessionStore sessions,
                    IAccountSafetyQuestionsService questions,
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

                    SafetyQuestionsOutcome outcome = await questions
                        .SaveAsync(
                            accountId.Value,
                            body.Question1!.Value,
                            body.Answer1!,
                            body.Question2!.Value,
                            body.Answer2!,
                            body.CurrentPassword!,
                            body.Code,
                            ct
                        )
                        .ConfigureAwait(false);

                    return outcome == SafetyQuestionsOutcome.Succeeded
                        ? TypedResults.Ok(EmptyResponse.Instance)
                        : TypedResults.BadRequest(new ApiErrorResponse(Describe(outcome)));
                }
            )
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("SaveSafetyQuestions")
            .WithSummary("Set the account's two security questions, against the current password.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/safetyquestions/disable",
                async Task<
                    Results<Ok<EmptyResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    SafetyQuestionsDisableRequest body,
                    WebApiSessionStore sessions,
                    IAccountSafetyQuestionsService questions,
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

                    SafetyQuestionsOutcome outcome = await questions
                        .ClearAsync(accountId.Value, body.CurrentPassword!, body.Code, ct)
                        .ConfigureAwait(false);

                    return outcome == SafetyQuestionsOutcome.Succeeded
                        ? TypedResults.Ok(EmptyResponse.Instance)
                        : TypedResults.BadRequest(new ApiErrorResponse(Describe(outcome)));
                }
            )
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("DisableSafetyQuestions")
            .WithSummary("Remove the account's security questions, against the current password.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/safetyquestions/unlock",
                async Task<
                    Results<Ok<EmptyResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    SafetyQuestionsUnlockRequest body,
                    WebApiSessionStore sessions,
                    IAccountSafetyQuestionsService questions,
                    IAccountTrustedLocationService locations,
                    IAccountSafetyLockService locks,
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

                    SafetyQuestionsOutcome outcome = await questions
                        .VerifyAsync(accountId.Value, body.Answer1!, body.Answer2!, ct)
                        .ConfigureAwait(false);

                    if (outcome != SafetyQuestionsOutcome.Succeeded)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse(Describe(outcome)));
                    }

                    // The session first: it is what every later request is judged on, and it is the
                    // one the visitor is waiting for. Trusting the PLACE is the extra they opted
                    // into with habbo.com's second radio.
                    sessions.MarkTrusted(ctx.SessionId());

                    if (body.Trust == true)
                    {
                        await locations
                            .TrustAsync(
                                accountId.Value,
                                locations.Fingerprint(ctx.RemoteIp(), ctx.UserAgent()),
                                ct
                            )
                            .ConfigureAwait(false);
                    }

                    await locks.ReleaseAsync(accountId.Value, ct).ConfigureAwait(false);

                    return TypedResults.Ok(EmptyResponse.Instance);
                }
            )
            // The same policy as sign-in, and for the same reason: two short answers are guessable
            // at the same rate as a password if nothing holds the door.
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("UnlockSafetyQuestions")
            .WithSummary("Answer the security questions, lifting the lock for this session.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/trustedlocations/reset",
                async Task<
                    Results<
                        Ok<TrustedLocationsResetResponse>,
                        BadRequest<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IAccountTrustedLocationService locations,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    // An untrusted session must not be able to clear the list: forgetting every
                    // place is how a thief would make sure the owner is challenged on their next
                    // sign-in, from their own home.
                    if (!sessions.IsTrusted(ctx.SessionId()))
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("safetylock.required"));
                    }

                    int forgotten = await locations
                        .ResetAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(new TrustedLocationsResetResponse(forgotten));
                }
            )
            .WithName("ResetTrustedLocations")
            .WithSummary("Forget every place this account has answered its questions from.")
            .WithTags(TagUser);
    }

    /// <summary>The wire code for a refusal. One place, so the four routes cannot word it three ways.</summary>
    private static string Describe(SafetyQuestionsOutcome outcome) =>
        outcome switch
        {
            SafetyQuestionsOutcome.MfaRequired => "pocket.auth.mfa_required",
            SafetyQuestionsOutcome.InvalidCode => "pocket.auth.invalid_code",
            SafetyQuestionsOutcome.WrongPassword => "pocket.auth.wrong_password",
            SafetyQuestionsOutcome.InvalidQuestions => "invalid_request",
            SafetyQuestionsOutcome.EmptyAnswer => "invalid_request",
            SafetyQuestionsOutcome.NotConfigured => "safetylock.not_configured",
            SafetyQuestionsOutcome.WrongAnswers => "safetylock.invalid_answer",
            _ => "invalid_request",
        };

    /// <summary>
    /// The sign-in address: what it is, and changing it. habbo.com's own paths
    /// (<c>/settings/email</c>, <c>/settings/email/change</c>) have a third for resending a
    /// verification, which is not here because nothing in this server can send one.
    /// </summary>
    private static void MapEmail(WebApplication app)
    {
        app.MapGet(
                "/api/user/email",
                async Task<Results<Ok<AccountEmailResponse>, UnauthorizedError>> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IAccountEmailService emails,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    string? email = await emails
                        .GetAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    // Verified is always false — see AccountEmailResponse.
                    return TypedResults.Ok(
                        new AccountEmailResponse(email ?? string.Empty, Verified: false)
                    );
                }
            )
            .WithName("GetEmail")
            .WithSummary("The address the signed-in account uses.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/email/change",
                async Task<
                    Results<
                        Ok<AccountEmailResponse>,
                        BadRequest<ApiErrorResponse>,
                        Conflict<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    ChangeEmailRequest body,
                    WebApiSessionStore sessions,
                    IAccountEmailService emails,
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
                        return TypedResults.BadRequest(
                            new ApiErrorResponse("pocket.auth.missing_credentials")
                        );
                    }

                    EmailChangeResult result = await emails
                        .ChangeAsync(
                            accountId.Value,
                            body.CurrentPassword!,
                            body.Email!,
                            body.Code,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (result.Succeeded)
                    {
                        return TypedResults.Ok(
                            new AccountEmailResponse(body.Email!.Trim(), Verified: false)
                        );
                    }

                    // An address someone else holds is the one refusal that is not the caller's
                    // mistake to correct by typing more carefully, so it keeps its own status.
                    return result.Outcome == EmailChangeOutcome.Taken
                        ? TypedResults.Conflict(new ApiErrorResponse("email_taken"))
                        : TypedResults.BadRequest(
                            new ApiErrorResponse(
                                result.Outcome switch
                                {
                                    EmailChangeOutcome.MfaRequired => "pocket.auth.mfa_required",
                                    EmailChangeOutcome.InvalidCode => "pocket.auth.invalid_code",
                                    EmailChangeOutcome.Invalid => "invalid_request",
                                    _ => "pocket.auth.wrong_password",
                                }
                            )
                        );
                }
            )
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("ChangeEmail")
            .WithSummary("Change the sign-in address, against the current password.")
            .WithTags(TagUser);
    }

    /// <summary>
    /// The selected avatar's preferences, on habbo.com's own two paths. One field today — see
    /// <see cref="PlayerPreferencesResponse"/> for why the other six are not offered rather than
    /// offered and dropped.
    /// </summary>
    private static void MapPreferences(WebApplication app)
    {
        app.MapGet(
                "/api/user/preferences",
                async Task<
                    Results<
                        Ok<PlayerPreferencesResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    bool? visible = await players
                        .GetProfileVisibleAsync(playerId.Value, ct)
                        .ConfigureAwait(false);

                    return visible is null
                        ? TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"))
                        : TypedResults.Ok(new PlayerPreferencesResponse(visible.Value));
                }
            )
            .WithName("GetPreferences")
            .WithSummary("The selected avatar's privacy preferences.")
            .WithTags(TagUser);

        // habbo.com's own `/api/user/profile`, and the reason it exists rather than the page reusing
        // the public read: `ProfileController` picks between the two on
        // `Session.hasSession() && profile.uniqueId === user.uniqueId`. Hiding a profile hides it
        // from VISITORS. A player looking at their own must still see it, and the public route — which
        // correctly answers four empty lists for a hidden profile — cannot tell them apart.
        app.MapGet(
                "/api/user/profile",
                async Task<
                    Results<Ok<PlayerProfile>, NotFound<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    IWebApiProfileService profiles,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    // No id on the route, deliberately. An id would make this "read anyone's profile
                    // ignoring their privacy setting" and the only thing standing between that and a
                    // scraper would be a check somebody could forget to write.
                    PlayerProfile? profile = await profiles
                        .GetOwnProfileAsync(playerId.Value, ct)
                        .ConfigureAwait(false);

                    return profile is null
                        ? TypedResults.NotFound(new ApiErrorResponse("user_not_found"))
                        : TypedResults.Ok(profile);
                }
            )
            .WithName("OwnProfile")
            .WithSummary("The signed-in avatar's own profile, private or not.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/preferences/save",
                async Task<
                    Results<
                        Ok<PlayerPreferencesResponse>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    SavePreferencesRequest body,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    bool saved = await players
                        .SetProfileVisibleAsync(playerId.Value, body.ProfileVisible!.Value, ct)
                        .ConfigureAwait(false);

                    return saved
                        ? TypedResults.Ok(new PlayerPreferencesResponse(body.ProfileVisible!.Value))
                        : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                }
            )
            .WithName("SavePreferences")
            .WithSummary("Publish or hide the selected avatar's web profile.")
            .WithTags(TagUser);
    }

    /// <summary>
    /// The paid shop. Five routes, and the division between them is the whole security model: the
    /// catalogue and the webhook are anonymous, the orders belong to a session, and only the webhook
    /// can make an order paid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is deliberately no route that confirms a payment from the browser. habbo.com sends the
    /// visitor back from the provider to a page, and a page is something anybody can open: the return
    /// URL here lands on <c>GET …/orders/{id}</c>, which REPORTS the state and cannot change it.
    /// </para>
    /// <para>
    /// The webhook is anonymous because the provider has no session, and it is safe because it is
    /// signed — see <c>WebhookSignature</c>. It answers 404 for an unknown or unconfigured provider,
    /// 401 for a signature that does not verify, and 204 for everything it accepted, including a
    /// notification it had already seen. A provider retries on anything that is not a 2xx, so saying
    /// "already done" any other way would cause a retry storm over work that is finished.
    /// </para>
    /// </remarks>
    private static void MapShop(WebApplication app)
    {
        app.MapGet(
                "/api/public/shop/products",
                async Task<Ok<ShopCatalog>> (IShopService shop, CancellationToken ct) =>
                    TypedResults.Ok(await shop.GetCatalogAsync(ct).ConfigureAwait(false))
            )
            .WithName("ShopProducts")
            .WithSummary("What the hotel sells, grouped into the store page's sections.")
            .WithTags(TagShop);

        app.MapPost(
                "/api/user/shop/orders",
                async Task<
                    Results<
                        Ok<ShopOrderStart>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    StartOrderRequest body,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    IShopService shop,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    ShopOrderResult result = await shop.StartOrderAsync(
                            playerId.Value,
                            body.ProductCode!,
                            ct
                        )
                        .ConfigureAwait(false);

                    return result.Start is not null
                        ? TypedResults.Ok(result.Start)
                        : TypedResults.BadRequest(new ApiErrorResponse(Describe(result.Refusal)));
                }
            )
            .RequireRateLimiting(ShopOrderRateLimitPolicy)
            .WithName("StartShopOrder")
            .WithSummary("Open an order for one product and start its payment.")
            .WithTags(TagShop);

        app.MapGet(
                "/api/user/shop/orders",
                async Task<
                    Results<
                        Ok<IReadOnlyList<ShopOrder>>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    IShopService shop,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    return playerId is null
                        ? refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"))
                        : TypedResults.Ok(
                            await shop.GetOrdersAsync(playerId.Value, ct).ConfigureAwait(false)
                        );
                }
            )
            .WithName("ShopOrders")
            .WithSummary("This avatar's orders, newest first.")
            .WithTags(TagShop);

        app.MapGet(
                "/api/user/shop/orders/{orderId}",
                async Task<Results<Ok<ShopOrder>, NotFound<ApiErrorResponse>, UnauthorizedError>> (
                    string orderId,
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    IShopService shop,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("order_not_found"));
                    }

                    // The page the provider sends the browser back to polls this. It reads state; it
                    // does not set it. Someone else's order is a 404, never a 403: an order id is an
                    // identifier, not a permission.
                    ShopOrder? order = await shop.GetOrderAsync(playerId.Value, orderId, ct)
                        .ConfigureAwait(false);

                    return order is null
                        ? TypedResults.NotFound(new ApiErrorResponse("order_not_found"))
                        : TypedResults.Ok(order);
                }
            )
            .WithName("ShopOrder")
            .WithSummary("One order's state. What the return-from-payment page reads.")
            .WithTags(TagShop);

        app.MapPost(
                "/api/user/shop/voucher",
                async Task<
                    Results<
                        Ok<EmptyResponse>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    RedeemVoucherRequest body,
                    WebApiSessionStore sessions,
                    IWebApiPlayerService players,
                    IShopService shop,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refusal) = await SelectedPlayerAsync(
                            ctx,
                            sessions,
                            players,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refusal is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    if (body is null || !body.IsValid)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("invalid_request"));
                    }

                    string? error = await shop.RedeemVoucherAsync(playerId.Value, body.Code!, ct)
                        .ConfigureAwait(false);

                    return error is null
                        ? TypedResults.Ok(EmptyResponse.Instance)
                        : TypedResults.BadRequest(new ApiErrorResponse(error));
                }
            )
            // Same limiter as opening an order: a prepaid code is guessable in a way a product code
            // is not, and an unthrottled redeem route is a code-guessing oracle.
            .RequireRateLimiting(ShopOrderRateLimitPolicy)
            .WithName("RedeemVoucher")
            .WithSummary("Redeem a prepaid code.")
            .WithTags(TagShop);

        app.MapPost(
                "/api/public/shop/webhook/{provider}",
                async Task<
                    Results<
                        NoContent,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (string provider, HttpContext ctx, IShopService shop, CancellationToken ct) =>
                {
                    string body = await ReadBodyAsync(ctx.Request, ct).ConfigureAwait(false);

                    ShopWebhookRequest request = new(
                        provider,
                        body,
                        ctx.Request.Headers.ToDictionary(
                            header => header.Key,
                            header => header.Value.ToString(),
                            System.StringComparer.OrdinalIgnoreCase
                        )
                    );

                    ShopWebhookOutcome outcome = await shop.HandleWebhookAsync(request, ct)
                        .ConfigureAwait(false);

                    return outcome switch
                    {
                        ShopWebhookOutcome.Accepted => TypedResults.NoContent(),
                        ShopWebhookOutcome.BadSignature => new UnauthorizedError("bad_signature"),
                        ShopWebhookOutcome.UnknownProvider => TypedResults.NotFound(
                            new ApiErrorResponse("unknown_provider")
                        ),
                        ShopWebhookOutcome.UnknownOrder => TypedResults.NotFound(
                            new ApiErrorResponse("order_not_found")
                        ),
                        _ => TypedResults.BadRequest(new ApiErrorResponse("amount_mismatch")),
                    };
                }
            )
            .WithName("ShopWebhook")
            .WithSummary("A payment provider's signed notification. The only thing that grants.")
            .WithTags(TagShop);
    }

    /// <summary>
    /// The raw request body, capped. Read as TEXT and handed on unparsed, because the signature is
    /// over the bytes the provider sent: a JSON round trip changes whitespace and key order, and a
    /// signature that then fails looks exactly like an attack.
    /// </summary>
    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        // An unbounded read on an anonymous route is a way to spend the hotel's memory for the price
        // of a POST. No provider's notification is anywhere near this large.
        const int MaximumBodyBytes = 64 * 1024;

        request.EnableBuffering();

        byte[] buffer = new byte[MaximumBodyBytes];
        int read = await request
            .Body.ReadAtLeastAsync(buffer, MaximumBodyBytes, throwOnEndOfStream: false, ct)
            .ConfigureAwait(false);

        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>The refusal code the site turns into French.</summary>
    private static string Describe(ShopOrderRefusal refusal) =>
        refusal switch
        {
            ShopOrderRefusal.UnknownProduct => "unknown_product",
            ShopOrderRefusal.TooManyOpen => "too_many_open_orders",
            _ => "shop_unavailable",
        };

    /// <summary>
    /// The avatar a session's calls act on: the one it picked, or the account's first when it never
    /// went through the picker. Three routes make the same choice — the SSO ticket, the purse and
    /// the preferences — and it had been written out three times.
    /// </summary>
    /// <returns>
    /// The player id, or null with the refusal to answer: a 401 when there is no session at all, and
    /// otherwise null for "signed in, owns no avatar", which each caller words for itself.
    /// </returns>
    internal static async Task<(int? PlayerId, IResult? Refusal)> SelectedPlayerAsync(
        HttpContext ctx,
        WebApiSessionStore sessions,
        IWebApiPlayerService players,
        CancellationToken ct
    )
    {
        int? accountId = ctx.AccountId(sessions);

        if (accountId is null)
        {
            return (null, Unauthorized());
        }

        int? selected = sessions.GetSelectedPlayer(ctx.SessionId());

        if (selected is not null)
        {
            return (selected, null);
        }

        System.Collections.Generic.List<AvatarInfo> owned = await players
            .GetAvatarsForAccountAsync(accountId.Value, ct)
            .ConfigureAwait(false);

        return owned.Count == 0
            ? (null, null)
            : (int.Parse(owned[0].UniqueId, CultureInfo.InvariantCulture), null);
    }

    /// <summary>
    /// The second factor, on habbo.com's own paths. <c>IAccountMfaService</c> has had the whole
    /// feature — begin, confirm, verify, disable — since the dashboard needed it, and sign-in has
    /// always answered <c>pocket.auth.mfa_required</c> against it. Nothing had ever exposed the
    /// enrolment half to the website, so the settings page could only show a grey button.
    /// </summary>
    private static void MapTwoFactor(WebApplication app)
    {
        app.MapGet(
                "/api/user/twofactor",
                async Task<Results<Ok<TwoFactorStatusResponse>, UnauthorizedError>> (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IAccountMfaService mfa,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    return TypedResults.Ok(
                        new TwoFactorStatusResponse(
                            await mfa.IsEnabledAsync(accountId.Value, ct).ConfigureAwait(false)
                        )
                    );
                }
            )
            .WithName("TwoFactorStatus")
            .WithSummary("Whether the signed-in account has a second factor.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/twofactor/startregistration",
                async Task<
                    Results<
                        Ok<TwoFactorEnrolmentResponse>,
                        Conflict<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    WebApiSessionStore sessions,
                    IAccountMfaService mfa,
                    CancellationToken ct
                ) =>
                {
                    int? accountId = ctx.AccountId(sessions);

                    if (accountId is null)
                    {
                        return Unauthorized();
                    }

                    // Refused rather than silently reissued: a second secret handed to a session
                    // that already has a factor is how a stolen cookie would install its own.
                    // Replacing means disabling first, which demands a code from the stored one.
                    if (await mfa.IsEnabledAsync(accountId.Value, ct).ConfigureAwait(false))
                    {
                        return TypedResults.Conflict(
                            new ApiErrorResponse("pocket.auth.mfa_already_enabled")
                        );
                    }

                    MfaEnrolment enrolment = await mfa.BeginEnrolmentAsync(accountId.Value, ct)
                        .ConfigureAwait(false);

                    return TypedResults.Ok(
                        new TwoFactorEnrolmentResponse(enrolment.Secret, enrolment.Uri)
                    );
                }
            )
            .WithName("TwoFactorStart")
            .WithSummary("Begin enrolment: a secret and its otpauth URI, stored nowhere yet.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/twofactor/enable",
                async Task<
                    Results<Ok<EmptyResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    TwoFactorEnableRequest body,
                    WebApiSessionStore sessions,
                    IAccountMfaService mfa,
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

                    bool confirmed = await mfa.ConfirmEnrolmentAsync(
                            accountId.Value,
                            body.Secret!,
                            body.Code!,
                            ct
                        )
                        .ConfigureAwait(false);

                    return confirmed
                        ? TypedResults.Ok(EmptyResponse.Instance)
                        : TypedResults.BadRequest(new ApiErrorResponse("pocket.auth.invalid_code"));
                }
            )
            // Same policy as sign-in: both take a short secret and answer whether it was right, so
            // both are guessable at the same rate if nothing holds the door.
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("TwoFactorEnable")
            .WithSummary("Confirm enrolment with a code computed from the secret.")
            .WithTags(TagUser);

        app.MapPost(
                "/api/user/twofactor/disable",
                async Task<
                    Results<Ok<EmptyResponse>, BadRequest<ApiErrorResponse>, UnauthorizedError>
                > (
                    HttpContext ctx,
                    TwoFactorDisableRequest body,
                    WebApiSessionStore sessions,
                    IAccountMfaService mfa,
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

                    bool removed = await mfa.DisableAsync(accountId.Value, body.Code, ct)
                        .ConfigureAwait(false);

                    return removed
                        ? TypedResults.Ok(EmptyResponse.Instance)
                        : TypedResults.BadRequest(new ApiErrorResponse("pocket.auth.invalid_code"));
                }
            )
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("TwoFactorDisable")
            .WithSummary("Remove the second factor, against a code from the one stored.")
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

        app.MapPost(
                "/api/newuser/room/select",
                Ok<EmptyResponse> () => TypedResults.Ok(EmptyResponse.Instance)
            )
            .WithName("RoomSelect")
            .WithSummary("Onboarding room selection (currently a no-op).")
            .WithTags(TagNewUser);
    }

    private static UnauthorizedError Unauthorized() => new("unauthorized");
}
