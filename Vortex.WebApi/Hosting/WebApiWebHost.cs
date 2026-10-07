using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Crypto;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Observability;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// Hosts the client-facing web API as a self-contained ASP.NET Core (Kestrel) application running
/// inside the Orleans generic host — exactly like <c>Vortex.Dashboard.API</c>'s web host. It is its own
/// isolated minimal web app listening on the configured host/port, but reuses the singletons
/// registered by <see cref="WebApiPlugin"/> in the plugin's container — its own, plus those it
/// borrows from the hotel — by forwarding them into the web app's DI. The HTTP surface is the minimal-API endpoints in <see cref="WebApiEndpoints"/>, documented
/// with Swagger/OpenAPI and hardened with CORS, rate limiting and optional HTTPS/HSTS. Disabled unless
/// <see cref="WebApiConfig.Enabled"/> is set.
/// </summary>
internal sealed class WebApiWebHost(
    IServiceProvider rootServices,
    IOptions<WebApiConfig> options,
    RequiredServiceGuard guard,
    ILogger<WebApiWebHost> logger
) : BackgroundService
{
    internal const string SERVICE_NAME = "Vortex web API";

    internal const string REQUIRED_OPTION_PATH =
        $"{WebApiConfig.SECTION_NAME}:{nameof(WebApiConfig.Required)}";

    internal const string ALLOW_INSECURE_OPTION_PATH =
        $"{WebApiConfig.SECTION_NAME}:{nameof(WebApiConfig.AllowInsecureRemoteHttp)}";

    private readonly WebApiConfig _config = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            return;
        }

        string httpPrefix = $"http://{_config.Host}:{_config.Port}";
        string prefixes = _config.HttpsEnabled
            ? $"{httpPrefix}, https://{_config.Host}:{_config.HttpsPort}"
            : httpPrefix;

        // Refuse cleartext off-box exposure before a socket is ever opened: this API carries
        // registration/login credentials and the session cookie.
        ListenerSecurityResult listener = ListenerSecurity.ValidateListener(
            SERVICE_NAME,
            _config.Host,
            _config.Port,
            _config.HttpsEnabled,
            _config.AllowInsecureRemoteHttp,
            ALLOW_INSECURE_OPTION_PATH
        );

        if (!listener.IsAllowed)
        {
            Fail(new InvalidOperationException(listener.Message));
            return;
        }

        if (listener.Message is not null)
        {
            logger.LogWarning("{ListenerWarning}", listener.Message);
        }

        WebApplication app;

        try
        {
            app = BuildApp();
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
            "Vortex web API listening on {Prefixes} (Swagger UI at {HttpPrefix}/swagger)",
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
            _config.Enabled,
            _config.Required,
            ex,
            REQUIRED_OPTION_PATH
        );

    private WebApplication BuildApp()
    {
        string httpPrefix = $"http://{_config.Host}:{_config.Port}";
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseUrls(
            _config.HttpsEnabled
                ? new[] { httpPrefix, $"https://{_config.Host}:{_config.HttpsPort}" }
                : new[] { httpPrefix }
        );

        ConfigureKestrel(builder);

        builder.Logging.ClearProviders();

        ForwardSingletons(builder.Services);
        ConfigureForwardedHeaders(builder.Services);
        WebApiAppConfigurator.ConfigureServices(builder.Services, _config);

        WebApplication app = builder.Build();

        if (_config.UseForwardedHeaders)
        {
            // Must run before anything reads the scheme or the remote IP (HTTPS redirection,
            // rate limiting, auth).
            app.UseForwardedHeaders();
        }

        WebApiAppConfigurator.ConfigurePipeline(app, _config);
        WebApiEndpoints.Map(app);

        // After the API routes: the site's catch-all must never shadow /api, and the share URL is a
        // page, not an endpoint.
        WebApiSiteHosting.Map(app, _config);

        return app;
    }

    private void ConfigureKestrel(WebApplicationBuilder builder)
    {
        if (!_config.HttpsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.CertificatePath))
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
                    _config.CertificatePath,
                    _config.CertificatePassword
                )
            )
        );
    }

    /// <summary>
    /// Applies <see cref="ForwardedHeadersOptions"/> only when the operator has explicitly declared a
    /// reverse proxy. ASP.NET Core trusts no proxy by default, and processing
    /// <c>X-Forwarded-For</c>/<c>-Proto</c> from an unknown sender would let any client forge its
    /// source IP (which partitions the rate limiters) and claim a TLS-terminated request.
    /// </summary>
    private void ConfigureForwardedHeaders(IServiceCollection services)
    {
        if (!_config.UseForwardedHeaders)
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

            foreach (string proxy in _config.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out IPAddress? address))
                {
                    forwarded.KnownProxies.Add(address);
                }
            }

            foreach (string network in _config.KnownNetworks)
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

    /// <summary>
    /// Shares the web API singletons constructed in the parent container with the web app's DI, so
    /// endpoints resolve the very same instances (already wired to their EF dependencies) instead of
    /// rebuilding the object graph.
    /// </summary>
    private void ForwardSingletons(IServiceCollection services)
    {
        services.AddSingleton(rootServices.GetRequiredService<WebApiSessionStore>());
        services.AddSingleton(rootServices.GetRequiredService<IWebApiAuthService>());
        services.AddSingleton(rootServices.GetRequiredService<IWebApiPlayerService>());
        // The very instance the grains filter with: one compiled list, one reload.
        services.AddSingleton(rootServices.GetRequiredService<IWordFilter>());
        services.AddSingleton(rootServices.GetRequiredService<RequiredServiceGuard>());

        services.AddSingleton(
            rootServices.GetRequiredService<IDbContextFactory<VortexDbContext>>()
        );
        services.AddSingleton(options);
    }
}
