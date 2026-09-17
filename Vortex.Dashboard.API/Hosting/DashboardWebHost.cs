using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Platform;
using Vortex.Dashboard.API.Http;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Security;
using Vortex.Database.Backup;
using Vortex.Observability.Configuration;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Console;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
///     Hosts the admin dashboard API as a self-contained ASP.NET Core (Kestrel) application running inside
///     the Orleans generic host. It is intentionally isolated — its own minimal web app, listening on the
///     configured dashboard host/port — but reuses the singletons registered by <c>DashboardApiModule</c>
///     in the parent container by forwarding them into the web app's DI. The HTTP surface is a set of
///     minimal-API endpoints (see <see cref="DashboardEndpoints" />) documented with Swagger/OpenAPI, which
///     gives operators interactive docs and provides the foundation for a future public API. Disabled
///     unless <see cref="ObservabilityConfig.DashboardEnabled" /> is set; the SPA front-end is served only
///     when <see cref="ObservabilityConfig.DashboardFrontendEnabled" /> is also set.
/// </summary>
internal sealed class DashboardWebHost(
    IServiceProvider rootServices,
    IOptions<ObservabilityConfig> options,
    RequiredServiceGuard guard,
    ILogger<DashboardWebHost> logger
) : BackgroundService
{
    internal const string SERVICE_NAME = "Vortex dashboard API";

    internal const string REQUIRED_OPTION_PATH =
        $"{ObservabilityConfig.SECTION_NAME}:{nameof(ObservabilityConfig.DashboardRequired)}";

    internal const string ALLOW_INSECURE_OPTION_PATH =
        $"{ObservabilityConfig.SECTION_NAME}:{nameof(ObservabilityConfig.DashboardAllowInsecureRemoteHttp)}";

    private readonly ObservabilityConfig _config = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.DashboardEnabled)
        {
            return;
        }

        DateTime startedAtUtc = DateTime.UtcNow;
        string httpPrefix = $"http://{_config.DashboardHost}:{_config.DashboardPort}";
        string prefixes = _config.DashboardHttpsEnabled
            ? $"{httpPrefix}, https://{_config.DashboardHost}:{_config.DashboardHttpsPort}"
            : httpPrefix;

        // Refuse cleartext off-box exposure before a socket is ever opened: the dashboard session
        // cookie grants dashboard.* capabilities, i.e. full operator access.
        ListenerSecurityResult listener = ListenerSecurity.ValidateListener(
            SERVICE_NAME,
            _config.DashboardHost,
            _config.DashboardPort,
            _config.DashboardHttpsEnabled,
            _config.DashboardAllowInsecureRemoteHttp,
            ALLOW_INSECURE_OPTION_PATH
        );

        if (!listener.IsAllowed)
        {
            Fail(new InvalidOperationException(listener.Message));
            return;
        }

        if (listener.Message is not null)
        {
            logger.LogWarning(VortexEventIds.DashboardFault, "{ListenerWarning}", listener.Message);
        }

        WebApplication app;

        try
        {
            app = BuildApp(httpPrefix, () => startedAtUtc);
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }

        try
        {
            await app.StartAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            Fail(ex);
            return;
        }

        guard.ReportStarted(SERVICE_NAME);

        logger.LogInformation(
            VortexEventIds.DashboardReady,
            "Vortex dashboard API listening on {Prefixes} (Swagger UI at {HttpPrefix}/swagger)",
            prefixes,
            httpPrefix
        );

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host shutting down.
        }

        await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Routes a build/start failure through the shared policy: required means the host comes down
    /// with a non-zero exit code, optional means the emulator is flagged degraded and carries on.
    /// </summary>
    private void Fail(Exception ex) =>
        guard.ReportStartupFailure(
            SERVICE_NAME,
            _config.DashboardEnabled,
            _config.DashboardRequired,
            ex,
            REQUIRED_OPTION_PATH
        );

    private WebApplication BuildApp(string httpPrefix, Func<DateTime> startedAtUtc)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(
            _config.DashboardHttpsEnabled
                ? [httpPrefix, $"https://{_config.DashboardHost}:{_config.DashboardHttpsPort}"]
                : [httpPrefix]
        );

        ConfigureKestrel(builder);

        // The dashboard web app keeps its own logging quiet; lifecycle messages come from the parent
        // logger above so they sit alongside the rest of the emulator output.
        builder.Logging.ClearProviders();

        ForwardSingletons(builder.Services);
        ConfigureForwardedHeaders(builder.Services);
        ConfigureAuth(builder.Services);
        ConfigureRateLimiting(builder.Services);
        ConfigureHttpsRedirection(builder.Services);
        ConfigureSwagger(builder.Services);

        WebApplication app = builder.Build();

        ConfigurePipeline(app);

        DashboardEndpoints.MapAuth(app);
        DashboardEndpoints.MapReadApi(app, startedAtUtc);
        DashboardEndpoints.MapOperations(app);
        DashboardEndpoints.MapMeta(app);
        DashboardEndpoints.MapAccountEndpoints(app);

        if (_config.DashboardFrontendEnabled)
        {
            DashboardEndpoints.MapFrontend(app);
        }

        return app;
    }

    /// <summary>
    ///     The response header every dashboard reply carries its correlation id in. A header rather
    ///     than only a body field because the refusals that matter most have no body: a 403 from the
    ///     authorization middleware and a 429 from the rate limiter never reach a handler.
    /// </summary>
    internal const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>
    ///     Services an endpoint injects directly rather than through a subject's class, plus the
    ///     dashboard's own infrastructure. Neither kind is discoverable from a base class or a
    ///     constructor parameter, so each is named.
    ///     <para>
    ///     Declared before <see cref="ForwardedServiceTypes" /> on purpose: static fields initialise
    ///     in declaration order, and the derived list reads this one.
    ///     </para>
    /// </summary>
    private static readonly Type[] DeclaredServiceTypes =
    [
        typeof(DashboardMonitoringReads),
        typeof(DashboardAuthService),
        typeof(DashboardSessionStore),
        typeof(DashboardAssetStore),
        typeof(DashboardAssetUrls),
        typeof(GamedataDocumentStore),
        typeof(DashboardAuditEmitter),
        typeof(IDatabaseBackupService),
        // The console stream endpoint injects this directly rather than going through an operations
        // class, and an endpoint parameter the container cannot resolve is read as a request body.
        typeof(ServerConsoleFeed),
        // The self-service second-factor endpoints act on the caller's own account, so there is no
        // operations class between them and these.
        typeof(IAccountMfaService),
        typeof(IAccountPasswordService),
    ];

    /// <summary>
    ///     Everything an endpoint delegate is allowed to ask for by parameter. The web app has its own
    ///     container, so a type missing from here is not merely unresolvable — minimal APIs decide what
    ///     a parameter *is* by asking the container whether it knows the type, and a type it does not
    ///     know is taken for the request body. On a GET that is fatal, and not at the endpoint:
    ///     inference runs while the middleware pipeline is being built, so one missing service takes
    ///     down the entire dashboard with "Body was inferred but the method does not allow inferred body
    ///     parameters". <c>DashboardEndpointServiceTests</c> maps every endpoint against exactly this
    ///     set so the omission is a failing test rather than a degraded emulator.
    /// </summary>
    /// <remarks>
    ///     Half derived, half declared, and the split is the point. The derived half is every subject's
    ///     own class, recognised by what it is rather than by being remembered: a read derives
    ///     <see cref="DashboardReads" />, a write takes an <see cref="OperationRunner" />. That half used
    ///     to be hand-written and grew by two entries per extracted subject, which is exactly the shape
    ///     the architecture rules say this mechanism must not take. The declared half is the handful of
    ///     services the dashboard borrows or injects directly; it does not grow with the subjects, and
    ///     writing it out says which they are.
    /// </remarks>
    internal static readonly Type[] ForwardedServiceTypes = BuildForwardedServiceTypes();

    private static Type[] BuildForwardedServiceTypes() =>
        [
            .. typeof(DashboardWebHost)
                .Assembly.GetTypes()
                .Where(IsSubjectService)
                .Concat(DeclaredServiceTypes)
                .Distinct()
                .OrderBy(type => type.Name, StringComparer.Ordinal),
        ];

    /// <summary>A class one subject owns: its reads, or its writes.</summary>
    private static bool IsSubjectService(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
        && (
            typeof(DashboardReads).IsAssignableFrom(type)
            || type.GetConstructors()
                .Any(constructor =>
                    constructor
                        .GetParameters()
                        .Any(parameter => parameter.ParameterType == typeof(OperationRunner))
                )
        );

    /// <summary>
    ///     Shares the dashboard singletons constructed in the parent container with the web app's DI, so
    ///     endpoints resolve the very same instances (which already have their Orleans/EF dependencies
    ///     satisfied) rather than rebuilding the object graph.
    /// </summary>
    private void ForwardSingletons(IServiceCollection services)
    {
        foreach (Type serviceType in ForwardedServiceTypes)
        {
            services.AddSingleton(serviceType, rootServices.GetRequiredService(serviceType));
        }

        services.AddSingleton(options);

        // Not in ForwardedServiceTypes: that list is the set of types an endpoint delegate may take
        // as a parameter, and DashboardEndpointServiceTests reads it as precisely that. These two are
        // resolved by the pipeline below, never by an endpoint signature — and forwarding them is not
        // optional, because the web app has its own container and ConfigurePipeline asks it directly.
        // Missing one is not a degraded feature: the dashboard fails to start.
        services.AddSingleton(rootServices.GetRequiredService<IVortexMetrics>());
        services.AddSingleton(rootServices.GetRequiredService<IVortexContextAccessor>());
    }

    private static void ConfigureAuth(IServiceCollection services)
    {
        // Our cookie/session scheme is the default, so the default authorization policy
        // (RequireAuthenticatedUser) already gates plain [Authorize]/RequireAuthorization() endpoints.
        services
            .AddAuthentication(DashboardAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DashboardAuthenticationHandler>(
                DashboardAuthenticationHandler.SchemeName,
                null
            );

        services.AddAuthorization(authorization =>
        {
            // One policy per capability, named after the capability string the endpoints declare;
            // a wildcard ("*") capability satisfies every policy. Driven straight off the canonical
            // list so a newly declared capability can never be missing a policy here.
            foreach (string capability in Capabilities.Dashboard.All)
            {
                authorization.AddPolicy(
                    capability,
                    policy =>
                        policy
                            .RequireAuthenticatedUser()
                            .RequireClaim(
                                DashboardAuthenticationHandler.CapabilityClaimType,
                                capability,
                                Capabilities.Wildcard
                            )
                );
            }
        });
    }

    private void ConfigureKestrel(WebApplicationBuilder builder)
    {
        if (!_config.DashboardHttpsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.DashboardCertificatePath))
        {
            // No explicit certificate: Kestrel falls back to the ASP.NET Core development
            // certificate. HttpsCertificateValidator refuses this outside Development at startup,
            // so reaching here means it is a deliberate local-dev configuration.
            return;
        }

        // Load eagerly and let failures propagate: BuildApp()'s caller routes them through the
        // required/optional policy. Previously a bad path or password silently skipped HTTPS
        // configuration, leaving a listener that looked like HTTPS but was never configured.
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.ConfigureHttpsDefaults(https =>
                https.ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                    _config.DashboardCertificatePath,
                    _config.DashboardCertificatePassword
                )
            )
        );
    }

    /// <summary>
    /// Applies <see cref="ForwardedHeadersOptions"/> only when the operator has explicitly declared a
    /// reverse proxy. ASP.NET Core trusts no proxy by default, and processing
    /// <c>X-Forwarded-For</c>/<c>-Proto</c> from an unknown sender would let any client forge its
    /// source IP (which partitions the login rate limiter) and claim a TLS-terminated request.
    /// </summary>
    private void ConfigureForwardedHeaders(IServiceCollection services)
    {
        if (!_config.DashboardUseForwardedHeaders)
        {
            return;
        }

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Defaults are loopback-only; clear them so the explicit allow-list below is authoritative.
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();

            foreach (string proxy in _config.DashboardKnownProxies)
            {
                if (IPAddress.TryParse(proxy, out IPAddress? address))
                {
                    forwarded.KnownProxies.Add(address);
                }
            }

            foreach (string network in _config.DashboardKnownNetworks)
            {
                string[] parts = network.Split('/', 2);

                if (
                    parts.Length == 2
                    && IPAddress.TryParse(parts[0], out IPAddress? prefix)
                    && int.TryParse(parts[1], out int length)
                )
                {
                    forwarded.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
                }
            }
        });
    }

    private void ConfigureRateLimiting(IServiceCollection services)
    {
        ObservabilityConfig.DashboardRateLimitOptions limit = _config.DashboardLoginRateLimit;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(
                DashboardEndpoints.LoginRateLimitPolicy,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limit.PermitLimit,
                            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                            QueueLimit = limit.QueueLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            AutoReplenishment = true,
                        }
                    )
            );

            // Same budget as login, different partition. These callers are already authenticated, so
            // the remote IP is the wrong key twice over: one attacker holding one stolen session can
            // rotate addresses, and an office behind a single NAT would lock each other out. The
            // account is what is under attack, so the account is what is counted. UseRateLimiter runs
            // after UseAuthentication, so the principal is there to read; the IP is only the fallback
            // for a request that never authenticated and is about to be rejected anyway.
            options.AddPolicy(
                DashboardEndpoints.MfaRateLimitPolicy,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.GetDashboardPrincipal()?.AccountId is int accountId
                            ? $"account:{accountId}"
                            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limit.PermitLimit,
                            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                            QueueLimit = limit.QueueLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            AutoReplenishment = true,
                        }
                    )
            );
        });
    }

    private void ConfigureHttpsRedirection(IServiceCollection services)
    {
        if (!_config.DashboardHttpsEnabled)
        {
            return;
        }

        services.AddHttpsRedirection(https => https.HttpsPort = _config.DashboardHttpsPort);
    }

    private static void ConfigureSwagger(IServiceCollection services)
    {
        // CreateSlimBuilder() trims the regex route constraint, but Swashbuckle's Swagger middleware
        // declares its document route with one — re-register it so the middleware can be built.
        services.Configure<RouteOptions>(routing =>
            routing.SetParameterPolicy<RegexInlineRouteConstraint>("regex")
        );

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(swagger =>
        {
            swagger.SwaggerDoc(
                "v1",
                new OpenApiInfo
                {
                    Title = "Vortex Dashboard API",
                    Version = "v1",
                    Description =
                        "Operations and observability API for the Vortex emulator. Authenticate via "
                        + "POST /api/login (issues the dash_session cookie); endpoints are authorized "
                        + "by dashboard.* capabilities.",
                }
            );

            swagger.AddSecurityDefinition(
                DashboardAuthenticationHandler.SchemeName,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = DashboardAuthenticationHandler.SessionCookieName,
                    Description =
                        "Session cookie issued by POST /api/login. Sent automatically by the browser "
                        + "on same-origin requests.",
                }
            );

            swagger.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference(
                        DashboardAuthenticationHandler.SchemeName,
                        document
                    ),
                    new List<string>()
                },
            });
        });
    }

    private void ConfigurePipeline(WebApplication app)
    {
        if (_config.DashboardUseForwardedHeaders)
        {
            // Must run before anything reads the scheme or the remote IP (HTTPS redirection,
            // rate limiting, auth, audit).
            app.UseForwardedHeaders();
        }

        string csp = DashboardSecurityHeaders.BuildCsp(
            app.Services.GetRequiredService<DashboardAssetUrls>().ImgSrcOrigins
        );
        DashboardAuditEmitter emitter = app.Services.GetRequiredService<DashboardAuditEmitter>();
        IVortexContextAccessor context = app.Services.GetRequiredService<IVortexContextAccessor>();

        // Reads had no error path at all: this app's own log providers are cleared (lifecycle output
        // comes from the parent logger), and the audit middleware at the bottom of this method does its
        // work *after* await next(), so a throwing endpoint skipped it too. A failing panel was a bare
        // 500 with no log line, no audit row and nothing to diagnose from -- the exact opposite of the
        // write path, which correlates and audits everything. Outermost so it wraps both.
        app.Use(
            async (ctx, next) =>
            {
                // One id for the whole request, opened here so everything below shares it: the
                // operation's own audit row, the HTTP access row the middleware at the bottom writes,
                // the log line, and the error payload. Before this each of those minted its own or
                // had none -- an access row for a failed read carried no correlation id at all, so a
                // 500 gave the operator an id that appeared in exactly one place and matched nothing
                // they could be sent to look at.
                using IVortexTraceScope requestScope = context.BeginScope(
                    $"{ctx.Request.Method} {ctx.Request.Path.Value ?? "/"}"
                );

                string correlationId = requestScope.Context.CorrelationId.Value;

                // Set on the way out rather than now: a bodiless refusal -- a 403 from the
                // authorization middleware, a 429 from the limiter -- never reaches a handler that
                // could put the id in a payload, and the header is the only place left to say it.
                ctx.Response.OnStarting(() =>
                {
                    if (!ctx.Response.Headers.ContainsKey(CorrelationHeader))
                    {
                        ctx.Response.Headers[CorrelationHeader] = correlationId;
                    }

                    return Task.CompletedTask;
                });

                try
                {
                    await next().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
                {
                    // The operator navigated away mid-request. Not a fault, and nothing left to write to.
                }
                catch (DashboardQueryException ex)
                {
                    await FailAsync(ctx, StatusCodes.Status400BadRequest, ex.Error, null)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await FailAsync(
                            ctx,
                            StatusCodes.Status500InternalServerError,
                            "internal_error",
                            ex
                        )
                        .ConfigureAwait(false);
                }
            }
        );

        async Task FailAsync(HttpContext ctx, int status, string error, Exception? ex)
        {
            string path = ctx.Request.Path.Value ?? "/";

            // The request's id, not a fresh one. Minting here produced an id the operator could quote
            // and nobody could find: the audit row written two lines down carried a different one.
            string correlationId =
                context.Current?.CorrelationId.Value ?? CorrelationId.New().Value;

            if (ex is not null)
            {
                logger.LogError(
                    VortexEventIds.DashboardFault,
                    ex,
                    "Dashboard request failed: {Method} {Path} ({CorrelationId})",
                    ctx.Request.Method,
                    path,
                    correlationId
                );
            }

            // Same rule as the access audit below: the API surface is what the audit trail is for, a
            // failing frontend asset is not a security event.
            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                emitter.Emit(path, AuditResult.Failed, status, "HttpAccess", ctx.ActorEmail());
            }

            if (ctx.Response.HasStarted)
            {
                // Headers are already on the wire; the client sees a truncated body and the log above
                // is the only record. Overwriting the status here would throw and lose even that.
                return;
            }

            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new { error, correlationId }).ConfigureAwait(false);
        }

        // Hardened headers for the SPA + JSON API. Swagger UI ships an inline bootstrap script, so it
        // is exempt from the strict CSP (it is operator-only and same-origin).
        app.Use(
            async (ctx, next) =>
            {
                if (
                    !(ctx.Request.Path.Value ?? "/").StartsWith(
                        "/swagger",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    DashboardSecurityHeaders.Apply(ctx.Response, csp);
                }

                await next().ConfigureAwait(false);
            }
        );

        DashboardControlPlaneMetrics.Use(app, app.Services.GetRequiredService<IVortexMetrics>());

        if (_config.DashboardHttpsEnabled)
        {
            if (_config.DashboardHstsEnabled)
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();
        }

        app.UseSwagger();
        app.UseSwaggerUI(ui =>
        {
            ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Vortex Dashboard API v1");
            ui.DocumentTitle = "Vortex Dashboard API";
        });

        // HTTP access audit trail, registered ABOVE authentication, authorization and the rate
        // limiter so that it wraps them.
        //
        // It used to sit below all three, which in this pipeline means inside them: a request the
        // authorization middleware rejected short-circuited there and never reached this, so the
        // denials -- the 401s, the 403s, the 429s, the requests most worth having a record of --
        // were the exact ones it could not see. Its own 401/403 branch had never been reachable.
        //
        // Login/logout audit themselves; operation success is audited by the operations classes
        // (with correlation id), so for operation routes only failures are logged here to avoid
        // duplicate records.
        app.Use(
            async (ctx, next) =>
            {
                await next().ConfigureAwait(false);

                string path = ctx.Request.Path.Value ?? "/";

                if (!path.StartsWith("/api/", StringComparison.Ordinal))
                {
                    return;
                }

                if (path is "/api/login" or "/api/logout")
                {
                    return;
                }

                int status = ctx.Response.StatusCode;
                bool isOperation =
                    path.StartsWith("/api/ops/", StringComparison.Ordinal)
                    || path.StartsWith("/api/v1/operations/", StringComparison.Ordinal);

                if (isOperation && status == StatusCodes.Status200OK)
                {
                    return;
                }

                AuditResult result = status switch
                {
                    StatusCodes.Status200OK => AuditResult.Success,
                    StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                        AuditResult.Denied,
                    _ => AuditResult.Failed,
                };

                emitter.Emit(path, result, status, "HttpAccess", ctx.ActorEmail());
            }
        );

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // Below authentication, because the principal it publishes does not exist above it, and above
        // the endpoints so anything they call can read it. This is the §5 boundary: an operation that
        // needs to make a security decision of its own reads the server's own view of the caller --
        // account, session, resolved capabilities, when that session last proved a second factor --
        // rather than the actor string a caller passed it.
        DashboardSessionStore actorSessions =
            app.Services.GetRequiredService<DashboardSessionStore>();

        app.Use(
            async (ctx, next) =>
            {
                DashboardPrincipal? principal = ctx.GetDashboardPrincipal();

                if (principal is null)
                {
                    // No live session. Anonymous requests, the login route, and the SPA's own assets
                    // all land here, and none of them has a security context to publish.
                    await next().ConfigureAwait(false);

                    return;
                }

                string? sessionId = ctx.Request.Cookies[
                    DashboardAuthenticationHandler.SessionCookieName
                ];

                using IDisposable scope = ActorSecurityContext.Enter(
                    new ActorSecurityContext
                    {
                        AccountId = principal.AccountId,
                        Email = principal.Email,
                        SessionId = sessionId,
                        Permissions = principal.Permissions,
                        SteppedUpAtUtc = actorSessions.SteppedUpAtUtc(sessionId),
                        CorrelationId = context.Current?.CorrelationId ?? CorrelationId.New(),
                    }
                );

                await next().ConfigureAwait(false);
            }
        );
    }
}
