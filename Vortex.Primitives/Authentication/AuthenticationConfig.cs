namespace Vortex.Primitives.Authentication;

public sealed class AuthenticationConfig
{
    public const string SECTION_NAME = "Vortex:Authentication";

    /// <summary>
    /// Secret used for HMAC-SHA256 hashing of remote IP addresses in auth events.
    /// Keep this secret stable and rotate as needed.
    /// </summary>
    public string IpHashSecret { get; init; } = "local-dev-ip-hash-secret";

    /// <summary>
    /// Fallback TTL (seconds) applied when a ticket has no <c>expires_at</c> column value.
    /// Tickets older than this are rejected as expired. Set to 0 to disable the fallback and
    /// accept null-expiry tickets unconditionally.
    /// Default: 30 seconds — tight enough to prevent replay, long enough for slow connections.
    /// </summary>
    public int TicketTtlSeconds { get; init; } = 30;

    /// <summary>
    /// When true, a non-locked ticket is deleted on its first successful use instead of having its
    /// expiry pushed forward. Default false: the client reads its ticket from <c>?sso=</c>, so a
    /// page reload presents the same ticket again, and single-use would turn every reload into a
    /// failed login. <see cref="TicketAbsoluteLifetimeSeconds"/> is the bound instead.
    /// </summary>
    public bool TicketSingleUse { get; init; }

    /// <summary>
    /// Optional absolute cap (seconds since a ticket's <c>CreatedAt</c>) on how far the sliding
    /// expiry refresh may push a ticket's validity. Unset (default) preserves today's behavior,
    /// where each successful use extends the ticket indefinitely into the future. Ignored when
    /// <see cref="TicketSingleUse"/> is true.
    /// </summary>
    public int? TicketAbsoluteLifetimeSeconds { get; init; }
}
