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
using Vortex.Crypto.Configuration;
using Vortex.Crypto.Ethereum;
using Vortex.Database.Context;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Chain;
using Vortex.Primitives.Crypto;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Shop;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Hosting;
using Vortex.WebApi.Notifications;
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
        builder.Services.AddSingleton<IAccountPasswordService>(new FakePasswordService());
        builder.Services.AddSingleton<IAccountMfaService>(Mfa);
        builder.Services.AddSingleton<IAccountEmailService>(Emails);
        builder.Services.AddSingleton<IAccountSafetyLockService>(SafetyLock);
        builder.Services.AddSingleton<IAccountSafetyQuestionsService>(SafetyQuestions);
        builder.Services.AddSingleton<IAccountTrustedLocationService>(TrustedLocations);
        builder.Services.AddSingleton<IShopService>(Shop);

        // A hotel with no chain, which is what every hotel runs by default — and the state the two
        // chain routes have to answer politely in rather than throw. Its absence here is what the
        // production 500 looked like from the inside: an unregistered service, read by a minimal
        // API as the request body, failing the whole endpoint.
        builder.Services.AddSingleton<IChainService>(new FakeChainService());

        // The REAL implementations, not fakes. Both are pure and deterministic — one derives an
        // address from a seed, the other recovers one from a signature — so a fake would only be a
        // second, weaker copy of arithmetic that is already pinned against ethers, and the wallet
        // tests would stop proving the thing they exist for.
        builder.Services.AddSingleton<IWalletProof>(new WalletProof());
        builder.Services.AddSingleton<IHotelWalletService>(
            new HotelWalletService(
                Options.Create(
                    new CryptoConfig
                    {
                        KeySize = "3",
                        PublicKey = "00",
                        PrivateKey = "00",
                        EnableServerToClientEncryption = false,
                        WalletMasterSeed =
                            "6f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
                    }
                ),
                NullLogger<HotelWalletService>.Instance
            )
        );
        builder.Services.AddSingleton<WalletLinkChallenges>();
        builder.Services.AddSingleton<RequiredServiceGuard>();

        // Mirrors WebApiWebHost.ForwardSingletons. Leaving a service out of that list is not a
        // resolution error at call time — a minimal API reads an unregistered parameter as the
        // request body — so this registration is also what keeps the two lists honest: drop it and
        // the report endpoint fails to build here, where it is cheap to find out.
        builder.Services.AddSingleton<IAuditSink>(Audit);

        // Both of these were forwarded in production and missing here, which is the half of the trap
        // the comment above does not cover: a [FromServices] parameter is NOT read as the body, so
        // the route builds, no test can reach it, and it answers 500 to everyone else. Found by
        // scripts/hooks/check-webapi-forwarding.mjs, which now compares the three lists.
        builder.Services.AddSingleton<IChainLeaderboard>(new FakeChainLeaderboard());
        builder.Services.AddSingleton<ICameraPhotoStore>(Photos);

        DbContexts = new TestDbContextFactory(
            new DbContextOptionsBuilder<VortexDbContext>()
                .UseInMemoryDatabase($"webapi-health-{Guid.NewGuid():N}")
                .Options
        );

        builder.Services.AddSingleton<IDbContextFactory<VortexDbContext>>(DbContexts);

        // The real service, not a fake: the article reads ARE the query — the status filter, the
        // publish date, the language fallback — so a fake would test the endpoint's plumbing and
        // none of the behaviour worth testing.
        builder.Services.AddSingleton<IWebApiArticleService>(
            new WebApiArticleService(DbContexts, NullLogger<WebApiArticleService>.Instance)
        );

        // Real for the same reason: a profile IS its four queries.
        builder.Services.AddSingleton<IWebApiProfileService>(
            new WebApiProfileService(DbContexts, NullLogger<WebApiProfileService>.Instance)
        );

        builder.Services.AddSingleton<IWebApiRoomService>(
            new WebApiRoomService(DbContexts, NullLogger<WebApiRoomService>.Instance)
        );

        // Real, like the two above: a photo read IS its query, and what is worth exercising against
        // a database is the condition it enforces — published, not deleted, owner not hidden.
        builder.Services.AddSingleton<IWebApiPhotoService>(new WebApiPhotoService(DbContexts));

        // Real for the same reason again: the feed's access rule (not deleted, author not hidden)
        // and its roots-only shape are exactly what is worth exercising against a database.
        builder.Services.AddSingleton<IWebApiFeedService>(
            new WebApiFeedService(DbContexts, Events)
        );

        // Real too, and the sink beside it: what is worth testing about a notification is that the
        // handler wrote the row the list then reads, which a fake on either side would hide.
        builder.Services.AddSingleton<IWebApiNotificationService>(
            new WebApiNotificationService(DbContexts)
        );
        builder.Services.AddSingleton<INotificationSink>(
            new NotificationSink(DbContexts, NullLogger<NotificationSink>.Instance)
        );

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
        // catch-all never shadows /api. It no-ops unless a test sets SiteRoot, which is why the share
        // URL had no test — the route was simply not mapped under the test server.
        WebApiSiteHosting.Map(_app, config);

        _app.Start();

        Client = _app.GetTestClient();
    }

    public WebApiSessionStore Sessions { get; }

    /// <summary>The second factor, shared with the endpoints so a test can enrol and then check.</summary>
    public FakeMfaService Mfa { get; } = new();

    /// <summary>The sign-in address, shared so a test can change it and read it back.</summary>
    public FakeEmailService Emails { get; } = new();

    /// <summary>The account safety lock, shared so a test can throw it and read it back.</summary>
    public FakeSafetyLockService SafetyLock { get; } = new();

    /// <summary>The security questions, shared so a test can arm an account before challenging it.</summary>
    public FakeSafetyQuestionsService SafetyQuestions { get; } = new();

    /// <summary>The trusted places, shared so a test can see what the unlock route remembered.</summary>
    public FakeTrustedLocationService TrustedLocations { get; } = new();

    /// <summary>The shop, shared so a test can read what the routes handed it.</summary>
    public FakeShopService Shop { get; } = new();

    /// <summary>What the endpoints emitted, in order, so a test can assert on the record itself.</summary>
    public RecordingAuditSink Audit { get; } = new();

    /// <summary>Seed a photo with <c>Photos.Add(bytes)</c> and fetch it by the key it returns.</summary>
    public FakeCameraPhotoStore Photos { get; } = new();

    /// <summary>
    /// Everything the feed announced. There is no bus here, so this is how a test asks whether a
    /// mention, a comment or a repost was raised at all.
    /// </summary>
    public FakeEventPublisher Events { get; } = new();

    /// <summary>The in-memory database behind the app, so a test can seed the rows it is about.</summary>
    public IDbContextFactory<VortexDbContext> DbContexts { get; }

    public HttpClient Client { get; }

    /// <summary>A client carrying a valid session cookie for an authenticated account.</summary>
    /// <param name="trusted">
    /// Whether the session has cleared the account's security questions. True is what a sign-in
    /// gives an account that has none, which is every test that does not say otherwise.
    /// </param>
    public HttpClient CreateAuthenticatedClient(bool trusted = true)
    {
        HttpClient client = _app.GetTestClient();
        string sessionId = Sessions.CreateSession(FakeAuthService.AccountId, trusted);
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
