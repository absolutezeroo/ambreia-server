namespace Vortex.Observability.Configuration;

/// <summary>
/// Strongly-typed options for the observability brick, bound from the <c>Vortex:Observability</c>
/// configuration section following the project's per-module options convention.
/// </summary>
public sealed class ObservabilityConfig
{
    public const string SECTION_NAME = "Vortex:Observability";

    /// <summary>Master switch for emitting <c>System.Diagnostics.Metrics</c> instruments.</summary>
    public bool MetricsEnabled { get; init; } = true;

    /// <summary>
    /// When true, an <c>Activity</c> span is started per operation (OpenTelemetry-ready). Spans are
    /// near-free when no listener is registered, so this can stay on by default.
    /// </summary>
    public bool TracingEnabled { get; init; } = true;

    /// <summary>Bounded capacity of the in-process audit queue before saturation drops occur.</summary>
    public int AuditChannelCapacity { get; init; } = 10_000;

    /// <summary>Maximum number of audit events persisted per database round-trip.</summary>
    public int AuditBatchSize { get; init; } = 200;

    /// <summary>Sliding window in seconds for live `/api/overview` aggregates.</summary>
    public int LiveStatsWindowSeconds { get; init; } = 60;

    /// <summary>Maximum number of top entries returned for live rooms/abusers.</summary>
    public int LiveStatsTopK { get; init; } = 5;

    /// <summary>Retry attempts when durable audit batch persistence fails.</summary>
    public int AuditWriteRetryAttempts { get; init; } = 2;

    /// <summary>Delay between retry attempts for failed durable audit writes.</summary>
    public int AuditWriteRetryDelayMs { get; init; } = 250;

    /// <summary>
    /// How long the audit trail is kept, in days. 0 keeps it for ever. This is what a dispute is
    /// settled with months later, so it outlives the privacy-bearing tables below by design.
    /// </summary>
    public int AuditRetentionDays { get; init; } = 180;

    /// <summary>
    /// How long item provenance is kept, in days. 0 (the default) keeps it for ever: an item's
    /// history is the item, and a rare furni outlives every retention window worth setting.
    /// </summary>
    public int ItemEventRetentionDays { get; init; }

    /// <summary>How long room chat is kept, in days. 0 keeps it for ever.</summary>
    public int ChatRetentionDays { get; init; } = 90;

    /// <summary>How long room visit history is kept, in days. 0 keeps it for ever.</summary>
    public int RoomVisitRetentionDays { get; init; } = 90;

    /// <summary>
    /// How long a finished commerce operation and its receipts are kept, in days. 0 keeps them for
    /// ever. Matches the audit trail, because an operation is the same kind of evidence: what a
    /// dispute about a purchase is settled with.
    /// <para>
    /// Only operations that finished uneventfully age out — <c>Completed</c> and
    /// <c>FailedBeforePivot</c>, with no event still owed. Anything <c>NeedsIntervention</c> is an
    /// operator's work list and is never swept; anything short of terminal is either in flight or
    /// stuck, and a stuck operation is precisely what somebody is meant to find (PERS-RCP-013).
    /// </para>
    /// </summary>
    public int CommerceRetentionDays { get; init; } = 180;

    /// <summary>Hours between retention sweeps. Minimum one hour.</summary>
    public int RetentionSweepIntervalHours { get; init; } = 24;

    /// <summary>Rows deleted per statement, so a sweep never holds the table for long.</summary>
    public int RetentionBatchSize { get; init; } = 5_000;

    /// <summary>
    /// Ceiling on rows removed from one table per sweep. A hotel enabling retention for the first
    /// time works through its backlog over several cycles rather than in one long delete.
    /// </summary>
    public int RetentionMaxRowsPerCycle { get; init; } = 200_000;

    /// <summary>Path to dead-letter file for batches that cannot be written to DB.</summary>
    public string AuditDeadLetterPath { get; init; } = "logs/audit-dead-letter.jsonl";

    /// <summary>
    /// Enables the native admin dashboard HTTP API. Off by default. Access is authenticated per
    /// account (email + password) and authorized by <c>dashboard.*</c> capabilities; there is no
    /// anonymous access and no shared token.
    /// </summary>
    public bool DashboardEnabled { get; init; }

    /// <summary>
    /// When true, a failure to build or start the dashboard takes the whole emulator down with a
    /// non-zero exit code instead of leaving it running without its admin surface. Off by default so
    /// existing deployments keep today's best-effort behaviour until they opt in.
    /// </summary>
    public bool DashboardRequired { get; init; }

    /// <summary>
    /// Explicit opt-in to serving the dashboard in cleartext on a non-local address. Off by default:
    /// the dashboard carries operator credentials and a session cookie that grants
    /// <c>dashboard.*</c> capabilities, so binding it off-box without TLS exposes full admin access
    /// to the network path. Startup is refused unless this is set or HTTPS is enabled.
    /// </summary>
    public bool DashboardAllowInsecureRemoteHttp { get; init; }

    /// <summary>
    /// Trusts <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> from a reverse proxy. Off by default —
    /// honouring those headers without knowing who may set them lets any client spoof its source IP
    /// (which drives the login rate limiter) and claim its request arrived over HTTPS. Enable only
    /// when a proxy you control is the sole ingress, and list it in
    /// <see cref="DashboardKnownProxies" /> or <see cref="DashboardKnownNetworks" />.
    /// </summary>
    public bool DashboardUseForwardedHeaders { get; init; }

    /// <summary>Proxy IP addresses whose forwarded headers are trusted.</summary>
    public string[] DashboardKnownProxies { get; init; } = [];

    /// <summary>Trusted proxy networks in CIDR form (e.g. <c>10.0.0.0/8</c>).</summary>
    public string[] DashboardKnownNetworks { get; init; } = [];

    /// <summary>
    /// Serves the bundled SPA front-end (shell HTML + hashed JS/CSS assets) from the dashboard
    /// listener. On by default. Set to false to expose only the JSON API (for example when the
    /// front-end is hosted elsewhere or you want metrics/API without serving the SPA); asset and
    /// SPA-shell requests then return 404 while <c>/api/*</c> stays available.
    /// </summary>
    public bool DashboardFrontendEnabled { get; init; } = true;

    /// <summary>Host the dashboard binds to. Keep "localhost" unless a reverse proxy fronts it.</summary>
    public string DashboardHost { get; init; } = "localhost";

    /// <summary>TCP port for the dashboard HTTP API.</summary>
    public int DashboardPort { get; init; } = 9000;

    /// <summary>Lifetime of an authenticated dashboard session in minutes (minimum 5).</summary>
    public int DashboardSessionLifetimeMinutes { get; init; } = 480;

    /// <summary>
    /// How recently an operator must have proved a second factor before the dashboard will run an
    /// operation marked critical -- clearing an account's factor, moving currency, changing the staff
    /// roster, running a console command, taking a database backup. Zero disables the requirement.
    /// </summary>
    /// <remarks>
    /// Off by default, and that is a deliberate choice rather than an oversight. Turning it on for a
    /// hotel whose operators have not enrolled a factor refuses every one of those operations until
    /// they do — correct, and not something to inflict on an existing installation by upgrading.
    /// A session's stamp is set only by <c>/api/v1/account/mfa/step-up</c>, never by logging in:
    /// an eight-hour session must not carry a factor proved eight hours ago.
    /// </remarks>
    public int DashboardStepUpMinutes { get; init; }

    /// <summary>When set, the dashboard also listens on HTTPS and redirects HTTP traffic to it.</summary>
    public bool DashboardHttpsEnabled { get; init; }

    /// <summary>TCP port for the dashboard HTTPS listener.</summary>
    public int DashboardHttpsPort { get; init; } = 9443;

    /// <summary>Optional PFX certificate used for the dashboard HTTPS listener; falls back to the dev certificate.</summary>
    public string? DashboardCertificatePath { get; init; }

    public string? DashboardCertificatePassword { get; init; }

    /// <summary>Emits HSTS headers for the dashboard (implies operators should only ever reach it over HTTPS).</summary>
    public bool DashboardHstsEnabled { get; init; }

    /// <summary>Fixed-window rate limit applied to dashboard <c>POST /api/login</c>.</summary>
    public DashboardRateLimitOptions DashboardLoginRateLimit { get; init; } =
        new DashboardRateLimitOptions
        {
            PermitLimit = 5,
            WindowSeconds = 60,
            QueueLimit = 0,
        };

    public sealed class DashboardRateLimitOptions
    {
        public int PermitLimit { get; init; } = 5;

        public int WindowSeconds { get; init; } = 60;

        public int QueueLimit { get; init; }
    }

    /// <summary>
    /// Origin every <c>*UrlTemplate</c> below that starts with <c>/</c> is resolved against — the one
    /// place a deployment says where the hotel's pictures live. Empty leaves those templates relative,
    /// so they resolve against whatever origin the dashboard itself is reached on.
    /// </summary>
    /// <remarks>
    /// One key rather than a host repeated in nine templates, and one origin rather than two: the
    /// front proxies the asset tree (<c>/c_images</c>, <c>/dcr</c>, <c>/gamedata</c>) and the imaging
    /// server (<c>/habbo-imaging</c>) on the same host, so a single value covers every template.
    /// A template that is already an absolute URL is left alone, which is what keeps a deployment
    /// free to point one picture somewhere else.
    /// </remarks>
    public string AssetBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Optional URL template for furniture icons shown in the operations picker. Use <c>{name}</c>
    /// as the definition-name placeholder, for example
    /// <c>/dcr/hof_furni/icons/{name}_icon.png</c> resolved against <see cref="AssetBaseUrl"/>, or an
    /// absolute <c>http://your-host/dcr/...</c>. Empty hides icons (a sprite-id tile is shown
    /// instead). When set, the icon host origin is added to the dashboard CSP <c>img-src</c> so the
    /// images are allowed to load.
    /// </summary>
    public string FurniIconUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Optional URL template for catalog page icons shown in the dashboard's catalog admin surface.
    /// Use <c>{id}</c> as the <c>CatalogPageEntity.Icon</c> placeholder, for example
    /// <c>http://vortex-assets.local/c_images/catalogue/icon_{id}.png</c>. Empty hides icons (a
    /// generic folder icon is shown instead). When set, the icon host origin is added to the
    /// dashboard CSP <c>img-src</c> so the images are allowed to load.
    /// </summary>
    public string CatalogIconUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Optional URL template for targeted-offer promo images shown in the dashboard's targeted-offer
    /// admin surface. Use <c>{file}</c> as the image filename placeholder, for example
    /// <c>http://vortex-assets.local/c_images/targetedoffers/{file}</c>. The admin then supplies just
    /// the filename instead of a full URL. Empty falls back to typing the whole URL. When set, the
    /// host origin is added to the dashboard CSP <c>img-src</c>.
    /// </summary>
    public string TargetedOfferImageUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Optional URL template for avatar images (player inspector heads) rendered from a player's
    /// figure string. Use <c>{figure}</c> as the placeholder, for example the Habbo imaging endpoint
    /// <c>http://vortex-assets.local/habbo-imaging/avatarimage?figure={figure}&amp;headonly=1&amp;size=m</c>.
    /// Empty hides the avatar (a generic icon is shown). When set, the host origin is added to the
    /// dashboard CSP <c>img-src</c>.
    /// </summary>
    public string AvatarImageUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Optional URL template for guild/group badges rendered from a group's badge code. Use
    /// <c>{badge}</c> as the placeholder, for example
    /// <c>http://vortex-assets.local/c_images/Badgeparts/generated/{badge}.gif</c>. Empty hides the
    /// badge (a generic icon is shown). When set, the host origin is added to the dashboard CSP
    /// <c>img-src</c>.
    /// </summary>
    public string GroupBadgeUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// URL template for an <b>achievement/player badge</b> image, with <c>{badge}</c> replaced by the
    /// badge code — e.g.
    /// <c>http://vortex-assets.local/c_images/album1584/{badge}.gif</c>. Deliberately separate from
    /// <see cref="GroupBadgeUrlTemplate"/>: a guild badge is composed on the fly by the imaging server
    /// from its parts, while these are static files shipped with the client. Empty hides the badge
    /// (the code is still shown). When set, the host origin is added to the dashboard CSP
    /// <c>img-src</c>.
    /// </summary>
    public string BadgeImageUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// URL template for a <b>hand item</b>, with <c>{figure}</c> and <c>{item}</c> replaced — e.g.
    /// <c>http://vortex-assets.local/habbo-imaging/avatarimage?figure={figure}&amp;action=crr={item}</c>.
    /// Hand items ship no icon of their own: the only picture the client ever draws of one is an
    /// avatar holding it, which is exactly what this renders. Empty hides the preview.
    /// </summary>
    public string HandItemImageUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// URL template for an <b>avatar effect</b>, with <c>{figure}</c> and <c>{effect}</c> replaced —
    /// e.g.
    /// <c>http://vortex-assets.local/habbo-imaging/avatarimage?figure={figure}&amp;effect={effect}</c>.
    /// Effects ship no icon either: like a hand item, the only picture of one is an avatar wearing
    /// it, which the imaging server renders. Empty hides the preview.
    /// </summary>
    public string AvatarEffectImageUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// URL template for <b>one garment on its own</b>, with <c>{figure}</c> replaced by a figure of
    /// a single segment such as <c>sh-290</c> — e.g.
    /// <c>http://vortex-assets.local/habbo-imaging/clothing/{figure}.png</c>.
    /// </summary>
    /// <remarks>
    /// Nothing wears it, unlike every other avatar template here: the imaging server draws the part
    /// alone and crops to its bounds, so the picker shows the garment rather than the same mannequin
    /// down every row. Empty hides those previews and leaves the picker usable — a set id is still
    /// perfectly writable by hand.
    /// </remarks>
    public string FigureImageUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// URL template for a <b>quest</b> image, with <c>{version}</c> replaced by the quest's
    /// <c>image_version</c> — e.g.
    /// <c>http://vortex-assets.local/c_images/Quests/{version}.png</c>. That column is exactly the
    /// asset's filename, which is why a quest with an empty one shows no picture in the client
    /// either. Empty hides the preview.
    /// </summary>
    public string QuestImageUrlTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Optional local filesystem root of the asset host (e.g. <c>C:\Laragon\www\vortex-assets</c>),
    /// used ONLY to enumerate available images for dashboard pickers (e.g. the targeted-offer promo
    /// image gallery, so operators pick a real image instead of typing a filename blind). Empty
    /// disables the pickers and operators fall back to typing. Never used to serve files.
    /// </summary>
    public string AssetsLocalRoot { get; init; } = string.Empty;

    /// <summary>Bounded capacity of the in-memory error-grouping queue.</summary>
    public int ErrorGroupingChannelCapacity { get; init; } = 10_000;

    /// <summary>Maximum number of grouped error rows persisted per writer batch.</summary>
    public int ErrorTrackingBatchSize { get; init; } = 150;

    /// <summary>Retry attempts when error-grouping writes fail.</summary>
    public int ErrorTrackingWriteRetryAttempts { get; init; } = 2;

    /// <summary>Delay between error-grouping write retries (milliseconds).</summary>
    public int ErrorTrackingWriteRetryDelayMs { get; init; } = 250;

    /// <summary>Latency threshold (ms) above which DB health is marked degraded.</summary>
    public int DatabaseProbeDegradedMs { get; init; } = 250;

    /// <summary>Latency threshold (ms) above which DB health is marked critical.</summary>
    public int DatabaseProbeCriticalMs { get; init; } = 1200;

    /// <summary>Latency threshold (ms) above which Orleans health is marked degraded.</summary>
    public int OrleansProbeDegradedMs { get; init; } = 350;

    /// <summary>Latency threshold (ms) above which Orleans health is marked critical.</summary>
    public int OrleansProbeCriticalMs { get; init; } = 1200;

    /// <summary>Incident detection rolling window in minutes.</summary>
    public int IncidentLookbackMinutes { get; init; } = 5;

    /// <summary>Degraded threshold for runtime error rate (errors/minute).</summary>
    public int ErrorSpikesDegradedPerMinute { get; init; } = 20;

    /// <summary>Critical threshold for runtime error rate (errors/minute).</summary>
    public int ErrorSpikesCriticalPerMinute { get; init; } = 80;

    /// <summary>Degraded threshold for login-failed rate (fails/minute).</summary>
    public int LoginFailedSpikesDegradedPerMinute { get; init; } = 8;

    /// <summary>Critical threshold for login-failed rate (fails/minute).</summary>
    public int LoginFailedSpikesCriticalPerMinute { get; init; } = 20;

    /// <summary>
    /// Degraded threshold for one account's audited actions (actions/minute). The other two spike
    /// signals are about the hotel; this one is about a single account, and it is what catches a
    /// script long before a person notices the pattern by hand.
    /// </summary>
    /// <remarks>
    /// Set well above what a person can produce. Playing normally, an account files a few audited
    /// actions a minute; sustaining dozens means something is driving the client. Too low and every
    /// busy player becomes an alert, which is the failure mode that gets alerting switched off.
    /// </remarks>
    public int AccountActionSpikesDegradedPerMinute { get; init; } = 40;

    /// <summary>Critical threshold for one account's audited actions (actions/minute).</summary>
    public int AccountActionSpikesCriticalPerMinute { get; init; } = 100;

    /// <summary>Number of top error groups returned in an incident snapshot.</summary>
    public int IncidentTopErrorGroups { get; init; } = 5;

    /// <summary>Rolling window in days for the top error groups query in an incident snapshot.</summary>
    public int IncidentErrorGroupWindowDays { get; init; } = 7;
}
