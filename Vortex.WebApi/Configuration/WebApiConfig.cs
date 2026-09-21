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

    public int MaxAvatarsPerAccount { get; set; } = 5;

    public string DefaultFigure { get; set; } =
        "hr-115-42.hd-195-19.ch-3030-82.lg-275-1408.fa-1201.ca-1804-64";

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
    /// the site: static files, an <c>index.html</c> fallback for the SPA's routes, and the share URL
    /// <c>/article/{slug}</c> that carries the Open Graph tags. Empty (the default) serves the API
    /// only.
    /// </summary>
    /// <remarks>
    /// Serving the site from here rather than from a separate web server is what keeps it on the same
    /// origin as <c>/api</c>. The session is an HttpOnly cookie, and a cross-origin fetch would not
    /// carry it — the site's own dev server proxies <c>/api</c> for exactly that reason.
    /// </remarks>
    public string SiteRoot { get; set; } = string.Empty;

    /// <summary>
    /// Absolute base URL of the asset host serving <c>c_images</c> (e.g.
    /// <c>http://vortex-assets.local</c>). Used for one thing: turning an article's stored relative
    /// picture path into the absolute URL an Open Graph tag requires. Empty means shared links carry
    /// no preview image — a link with a title and no picture, not a broken one.
    /// </summary>
    public string AssetBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The hotel's name as a shared link should announce it ("Vortex Hotel"). Fills
    /// <c>og:site_name</c>, which is the line a preview card prints above the title. Empty means the
    /// tag is omitted — the card then shows the bare domain, which is a worse card, not a broken one.
    /// </summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>
    /// Absolute base URL of the website (e.g. <c>https://vortex-hotel.online</c>). Used for one
    /// thing: the <c>external_url</c> in a Relic's ERC-721 metadata, which is the link a wallet or a
    /// marketplace offers back to the hotel. Empty means the field is omitted, and a Relic then has
    /// a name and a picture but nowhere to click through to.
    /// </summary>
    /// <remarks>
    /// Configured rather than taken from the request, which is what <c>og:url</c> does one file
    /// over. That works there because the share URL is served BY the site; this document is fetched
    /// at whatever host the contract's base URI names, and a deployment that puts the API on its own
    /// hostname — <c>webapi.</c>something — would otherwise publish that hostname as the hotel's
    /// address in every wallet holding a Relic.
    /// </remarks>
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

    /// <summary>
    /// Fixed-window rate limit applied to <c>POST /api/user/reports</c>.
    ///
    /// <para>
    /// Looser than the credential routes on purpose: this one is not guarding a secret, and a
    /// player hitting the ceiling mid-beta is a report that never arrives. It exists so a stuck
    /// retry loop or a bored tester cannot fill the audit table, not to ration honest reports.
    /// </para>
    /// </summary>
    public RateLimitOptions ReportRateLimit { get; set; } =
        new RateLimitOptions
        {
            PermitLimit = 10,
            WindowSeconds = 300,
            QueueLimit = 0,
        };

    /// <summary>
    /// Fixed-window rate limit applied to <c>POST /api/user/feed</c> and
    /// <c>DELETE /api/user/feed/{id}</c>.
    ///
    /// <para>
    /// The feed is the one surface here where the cost of a flood is paid by every reader rather
    /// than by the flooder: a page of twenty posts is twenty posts by whoever posted fastest. Ten
    /// a minute is far above what a person writing sentences reaches, and far below what a loop
    /// does.
    /// </para>
    /// <para>
    /// The delete route shares this budget rather than declaring one of its own, for a different
    /// reason: it verifies a six-digit second factor, and an unthrottled verifier is one an
    /// attacker walks through. Ten attempts a minute is the same "a handful of writes" the
    /// publishes get, and the two together are still more than a person does.
    /// </para>
    /// <para>
    /// Votes and reactions are not limited. They are cheap, they undo, and a reader legitimately
    /// fires several in a row going down a page; rationing those would punish normal use to no
    /// purpose.
    /// </para>
    /// </summary>
    public RateLimitOptions FeedPostRateLimit { get; set; } =
        new RateLimitOptions
        {
            PermitLimit = 10,
            WindowSeconds = 60,
            QueueLimit = 0,
        };

    /// <summary>
    /// Fixed-window rate limit applied to <c>POST /api/public/chain/rpc</c>.
    ///
    /// <para>
    /// Generous, because a wallet is chatty by nature: adding the network, drawing a balance and
    /// watching for a receipt is a dozen calls before the player has done anything, and a limit
    /// tight enough to matter would break the ordinary case. It is here to stop this route from
    /// becoming a free proxy for hammering the node — every call is a read or a broadcast, so the
    /// worst it can cost is the node's time.
    /// </para>
    /// </summary>
    public RateLimitOptions ChainRpcRateLimit { get; set; } =
        new RateLimitOptions
        {
            PermitLimit = 300,
            WindowSeconds = 60,
            QueueLimit = 0,
        };

    /// <summary>
    /// Fixed-window rate limit applied to <c>POST /api/user/shop/orders</c> and
    /// <c>POST /api/user/shop/voucher</c>.
    ///
    /// <para>
    /// Opening an order costs a row and charges nobody anything, so this is not the defence that
    /// matters there. It is the defence that matters on the voucher route, which takes a code and
    /// says whether it was real — an unthrottled version of that is a code-guessing oracle, and the
    /// two routes share a limiter so there is one number to get right.
    /// </para>
    /// </summary>
    public RateLimitOptions ShopOrderRateLimit { get; set; } =
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
