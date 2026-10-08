using System;

namespace Vortex.WebApi.Configuration;

/// <summary>
/// Configuration for the client-facing web API. The API is the public onboarding surface (login,
/// registration, SSO token), so the security knobs — CORS allow-list, HTTPS/HSTS and per-endpoint
/// rate limits — are all driven from here rather than hard-coded.
/// </summary>
public sealed class WebApiConfig
{
    public const string SECTION_NAME = "Vortex:WebApi";

    public bool Enabled { get; set; } = false;

    /// <summary>
    /// When true, a failure to build or start the API takes the whole emulator down with a non-zero
    /// exit code instead of leaving it running without its public onboarding surface. Off by default
    /// so existing deployments keep today's best-effort behaviour until they opt in.
    /// </summary>
    public bool Required { get; set; } = false;

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 8080;

    /// <summary>
    /// Explicit opt-in to serving this API in cleartext on a non-local address. Off by default: the
    /// API carries logins, passwords and session cookies, so binding it off-box without TLS exposes
    /// all of them to the network path. Startup is refused unless this is set or HTTPS is enabled.
    /// </summary>
    public bool AllowInsecureRemoteHttp { get; set; } = false;

    /// <summary>
    /// Trusts <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> from a reverse proxy. Off by default —
    /// honouring those headers without knowing who may set them lets any client spoof its source IP
    /// (which drives the rate limiters) and claim its request arrived over HTTPS. Enable this only
    /// when a proxy you control is the sole ingress, and list it in <see cref="KnownProxies"/> or
    /// <see cref="KnownNetworks"/>.
    /// </summary>
    public bool UseForwardedHeaders { get; set; } = false;

    /// <summary>Proxy IP addresses whose forwarded headers are trusted. Required when <see cref="UseForwardedHeaders"/> is set.</summary>
    public string[] KnownProxies { get; set; } = Array.Empty<string>();

    /// <summary>Trusted proxy networks in CIDR form (e.g. <c>10.0.0.0/8</c>).</summary>
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Exposes the Prometheus scraping endpoint (Vortex instruments + Orleans' own runtime counters)
    /// on this listener. Off by default, and deliberately stricter than <c>/health</c> next to it:
    /// health answers with three booleans, whereas a scrape hands over the whole internal telemetry
    /// surface — online population, active rooms, per-step room-tick timings, packet volumes — which
    /// is reconnaissance material. See <see cref="MetricsToken"/> for who may read it.
    /// </summary>
    public bool MetricsEnabled { get; set; } = false;

    /// <summary>Path the scraping endpoint is served on.</summary>
    public string MetricsPath { get; set; } = "/metrics";

    /// <summary>
    /// Serves <c>/swagger</c> and its OpenAPI document on this listener. Off by default, where it
    /// used to be unconditional.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document is not a secret the way a password is — every route it names is one a browser
    /// could reach anyway — but it is the entire attack surface written out for whoever finds the
    /// port: every path, every parameter, every refusal code, in the form a scanner reads directly.
    /// Publishing that is a choice a hotel should make, not inherit.
    /// </para>
    /// <para>
    /// Switching it off costs nothing: the website's types are generated from
    /// <c>Vortex.WebApi/openapi.json</c>, which a test writes out of the in-memory pipeline, so
    /// nobody needs a running hotel — let alone a public one — to hold the contract.
    /// </para>
    /// </remarks>
    public bool SwaggerEnabled { get; set; }

    /// <summary>
    /// Bearer token a scrape must present. When empty the endpoint answers loopback callers only,
    /// which is the safe default for a scraper running on the same box; set a token to let a remote
    /// Prometheus in. This is the one place the metrics endpoint is stricter than <c>/health</c>,
    /// whose listener-level controls (disabled by default, localhost-bound, cleartext off-box
    /// refused) it otherwise inherits unchanged.
    /// </summary>
    public string MetricsToken { get; set; } = string.Empty;

    /// <summary>
    /// How long a web session cookie stays valid. Was a day hard-coded in the session store, where
    /// nobody could shorten it for an exposed hotel or lengthen it for a private one.
    /// </summary>
    public int SessionLifetimeHours { get; set; } = 24;

    /// <summary>
    /// Origins permitted by CORS. Empty means "no cross-origin browser access"; a wildcard is never
    /// emitted because the API relies on credentialed (cookie) requests.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    /// <summary>When set, the API also listens on HTTPS and redirects HTTP traffic to it.</summary>
    public bool HttpsEnabled { get; set; } = false;

    public int HttpsPort { get; set; } = 8443;

    /// <summary>Optional PFX certificate used for the HTTPS listener; falls back to the dev certificate.</summary>
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    /// <summary>Emits HSTS headers (implies clients should only ever reach the API over HTTPS).</summary>
    public bool HstsEnabled { get; set; } = false;

    /// <summary>
    /// Absolute path to the website's built <c>dist/</c> folder. Set it and this listener also serves
    /// the site: static files and an <c>index.html</c> fallback for the SPA's routes. Empty (the
    /// default) serves the API only.
    /// </summary>
    /// <remarks>
    /// Serving the site from here rather than from a separate web server is what keeps it on the same
    /// origin as <c>/api</c>. The session is an HttpOnly cookie, and a cross-origin fetch would not
    /// carry it — the site's own dev server proxies <c>/api</c> for exactly that reason.
    /// </remarks>
    public string SiteRoot { get; set; } = string.Empty;

    /// <summary>
    /// Absolute base URL of the asset host serving <c>c_images</c> (e.g.
    /// <c>http://vortex-assets.local</c>). Nothing reads it any more: the Open Graph article page it
    /// fed is gone. Kept only so existing configuration still binds.
    /// </summary>
    public string AssetBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The site's display name (e.g. "Vortex Hotel"). Nothing reads it any more: the Open Graph
    /// article page it fed is gone. Kept only so existing configuration still binds.
    /// </summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>
    /// Absolute base URL of the website (e.g. <c>https://vortex-hotel.online</c>). Nothing reads it
    /// any more: no password-reset mail is sent. Kept only so existing configuration still binds.
    /// </summary>
    public string PublicSiteUrl { get; set; } = string.Empty;

    /// <summary>Fixed-window rate limit applied to <c>POST /api/public/authentication/login</c>.</summary>
    public RateLimitOptions LoginRateLimit { get; set; } =
        new RateLimitOptions
        {
            PermitLimit = 5,
            WindowSeconds = 60,
            QueueLimit = 0,
        };

    /// <summary>Fixed-window rate limit applied to <c>POST /api/public/registration/new</c>.</summary>
    public RateLimitOptions RegistrationRateLimit { get; set; } =
        new RateLimitOptions
        {
            PermitLimit = 3,
            WindowSeconds = 300,
            QueueLimit = 0,
        };

    /// <summary>Fixed-window rate limit applied to <c>GET /api/ssotoken</c>.</summary>
    public RateLimitOptions SsoTokenRateLimit { get; set; } =
        new RateLimitOptions
        {
            PermitLimit = 10,
            WindowSeconds = 60,
            QueueLimit = 0,
        };

    public sealed class RateLimitOptions
    {
        public int PermitLimit { get; set; } = 5;

        public int WindowSeconds { get; set; } = 60;

        public int QueueLimit { get; set; } = 0;
    }
}
