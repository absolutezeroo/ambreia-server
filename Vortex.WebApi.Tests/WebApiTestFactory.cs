using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Observability;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Hosting;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Spins up the web API's isolated ASP.NET Core app on an in-memory <see cref="TestServer"/>, built
/// through the very same <see cref="WebApiAppConfigurator"/> and <see cref="WebApiEndpoints"/> the
/// production host uses, so the tests exercise the real routing, validation, CORS and rate-limiting
/// pipeline against in-memory fakes instead of the database.
/// </summary>
internal sealed class WebApiTestFactory : IAsyncDisposable
{
    public const int LoginPermitLimit = 3;

    private readonly WebApplication _app;

    /// <param name="configure">
    /// Mutates the config before the app is built, for tests that need a non-default listener
    /// feature (the metrics scraping endpoint, for one) switched on.
    /// </param>
    /// <param name="remoteIp">
    /// Source address stamped on every request. <see cref="TestServer"/> leaves
    /// <c>Connection.RemoteIpAddress</c> null, which no real Kestrel request ever is, and endpoints
    /// that gate on loopback need it to be something.
    /// </param>
    public WebApiTestFactory(Action<WebApiConfig>? configure = null, IPAddress? remoteIp = null)
    {
        WebApiConfig config = new WebApiConfig
        {
            Enabled = true,
            AllowedOrigins = new[] { "https://client.test" },

            // On here and off in a deployment, deliberately: OpenApiDocumentTests reads the document
            // off this very pipeline to write openapi.json, which is how the website gets its types
            // without a hotel running. A deployed listener has no such need — see
            // WebApiConfig.SwaggerEnabled.
            SwaggerEnabled = true,
        };
        config.LoginRateLimit = new WebApiConfig.RateLimitOptions
        {
            PermitLimit = LoginPermitLimit,
            WindowSeconds = 60,
            QueueLimit = 0,
        };

        configure?.Invoke(config);

        Sessions = new WebApiSessionStore(Options.Create(config));

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(Sessions);
        builder.Services.AddSingleton<IWebApiAuthService>(new FakeAuthService(Sessions));
        builder.Services.AddSingleton<IWebApiPlayerService>(new FakePlayerService());
        builder.Services.AddSingleton<IOptions<WebApiConfig>>(Options.Create(config));

        builder.Services.AddSingleton<RequiredServiceGuard>();

        DbContexts = new TestDbContextFactory(
            new DbContextOptionsBuilder<VortexDbContext>()
                .UseInMemoryDatabase($"webapi-health-{Guid.NewGuid():N}")
                .Options
        );

        builder.Services.AddSingleton<IDbContextFactory<VortexDbContext>>(DbContexts);

        // Forwarded in production (WebApiWebHost.ForwardSingletons) — the same instance the grains
        // filter with. Registered here so check-webapi-forwarding.mjs finds the three lists agree.
        builder.Services.AddSingleton<IWordFilter>(WordFilter);

        WebApiAppConfigurator.ConfigureServices(builder.Services, config);

        _app = builder.Build();

        IPAddress caller = remoteIp ?? IPAddress.Loopback;
        _app.Use(
            async (ctx, next) =>
            {
                ctx.Connection.RemoteIpAddress = caller;

                await next().ConfigureAwait(false);
            }
        );

        WebApiAppConfigurator.ConfigurePipeline(_app, config);
        WebApiEndpoints.Map(_app);

        // Same call and same ordering as WebApiWebHost: after the API routes, so the site's
        // catch-all never shadows /api. It no-ops unless a test sets SiteRoot.
        WebApiSiteHosting.Map(_app, config);

        _app.Start();

        Client = _app.GetTestClient();
    }

    public WebApiSessionStore Sessions { get; }

    /// <summary>
    /// The hotel-wide word filter the routes see. Passes everything unless
    /// a test sets its <see cref="FakeWordFilter.Answer"/>.
    /// </summary>
    public FakeWordFilter WordFilter { get; } = new();

    /// <summary>The in-memory database behind the app, so a test can seed the rows it is about.</summary>
    public IDbContextFactory<VortexDbContext> DbContexts { get; }

    public HttpClient Client { get; }

    /// <summary>A client carrying a valid session cookie for an authenticated account.</summary>
    public HttpClient CreateAuthenticatedClient()
    {
        HttpClient client = _app.GetTestClient();
        string sessionId = Sessions.CreateSession(FakeAuthService.AccountId);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{WebApiHttpContextExtensions.SessionCookieName}={sessionId}"
        );

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
