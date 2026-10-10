using System;

namespace Vortex.Networking.Configuration;

public class NetworkingConfig
{
    public const string SECTION_NAME = "Vortex:Networking";

    /// <summary>
    ///     Default cap on a single client-declared packet body length, in bytes. Bounds
    ///     memory/CPU exposure from a malformed or hostile frame header.
    /// </summary>
    public const int DefaultMaxPacketBodyBytes = 65536;

    public int PingIntervalMilliseconds { get; init; } = 10000;

    /// <summary>
    ///     How long a session may stay silent before the heartbeat sends it a PING. Anything the
    ///     client sends counts as activity, so a session in normal use is never pinged.
    /// </summary>
    public TimeSpan IdleOkActivityWindow { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long a session may stay silent before it is closed as dead.
    ///     <see cref="TimeSpan.Zero" /> — the default — disables closing entirely.
    ///
    ///     Disabled on purpose, and this is not timidity: for a browser client, silence is not
    ///     evidence of death. Chrome FREEZES a backgrounded tab after roughly five minutes, which
    ///     stops all JavaScript — the WebSocket 'message' handler included — so the client cannot
    ///     answer a PING no matter how long it is given. It is not gone; it resumes the moment the
    ///     user comes back, and the socket is still open underneath. Closing on silence therefore
    ///     guarantees that every backgrounded tab is disconnected, which is the exact bug this
    ///     heartbeat was added to fix. Measured: pings answered for four minutes with the tab
    ///     hidden, then a hard stop, then a close 150 s later.
    ///
    ///     A genuinely dead peer is reported by the transport (the session's Closed handler), which
    ///     needs no timer. Set a non-zero value only if you have a reason the above does not cover.
    /// </summary>
    public TimeSpan PongTimeout { get; init; } = TimeSpan.Zero;

    /// <summary>
    ///     How long a connection may stay without an account — no accepted SSO ticket — before it
    ///     is closed. <see cref="TimeSpan.Zero" /> disables it.
    /// </summary>
    /// <remarks>
    ///     A real client sends its ticket within a second of connecting. A socket that never does is
    ///     a scanner, a half-open connection or an attacker holding a slot: with nothing closing it,
    ///     a few thousand of them fill <see cref="MaxTotalSessions" /> and the per-address cap for
    ///     good. Only the pre-account phase is bounded here; an authenticated session's silence is
    ///     the heartbeat's business (see <see cref="PongTimeout" />).
    /// </remarks>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The reverse proxies whose word about the client's address is taken: PROXY protocol v1/v2
    ///     on either listener (SuperSocket's <c>serverOptions:&lt;Server&gt;:enableProxyProtocol</c>
    ///     must be on for the header to be read), <c>X-Forwarded-For</c> on the WebSocket handshake.
    ///     Literal IP addresses. Empty — the default — means the transport address is the client's.
    /// </summary>
    /// <remarks>
    ///     Behind the TLS proxy the README recommends, every connection comes from the proxy's
    ///     address: the per-address cap (<see cref="MaxSessionsPerIpAddress" />) would hold the whole
    ///     hotel to 32 sessions and the audit would name one client. The list is what keeps a stranger
    ///     from writing a PROXY header or a header of their own and borrowing someone's address.
    /// </remarks>
    public string[] TrustedProxies { get; init; } = [];

    public int MaxPacketBodyBytes { get; init; } = DefaultMaxPacketBodyBytes;

    /// <summary>
    ///     Whether a packet that fails to parse has its bytes written to the log. Off, because the
    ///     first field of the SSO packet is the ticket: send the ticket, omit the int after it, and
    ///     the parser throws with the credential already sitting at offset 0 of the buffer the
    ///     error handler dumps. The 128-byte cap bounds the volume, not the secret, and hex is not
    ///     a one-way function.
    ///
    ///     Turn it on in a development configuration when diagnosing a parser against real bytes —
    ///     that is what the dump is for — and keep it off anywhere a real ticket can arrive. The
    ///     header, length, read position and error are logged either way, which is what says
    ///     <em>which</em> parser disagreed and where.
    /// </summary>
    public bool LogPacketPayloadOnError { get; init; }

    /// <summary>
    ///     How many packets one connection may send that fail to parse before it is closed. Zero
    ///     disables the budget.
    /// </summary>
    /// <remarks>
    ///     A client speaking this revision produces none: a parse failure means the two disagree
    ///     about the wire, and disagreeing ten times over is not a client with a bad connection, it
    ///     is something else. Counted for the life of the connection rather than per window, because
    ///     the honest number is zero and a window would only decide how slowly the budget refills.
    /// </remarks>
    public int MaxParseFailuresPerSession { get; init; } = 10;

    /// <summary>
    ///     How many concurrent sessions one remote address may hold. Zero disables the cap.
    /// </summary>
    /// <remarks>
    ///     The per-session rate limit (<c>RateLimitConfig</c>) bounds what ONE connection may send.
    ///     On its own it bounds nothing at the host level, because nothing bounded the number of
    ///     connections: a thousand sockets at fifty packets a second each is fifty thousand packets
    ///     a second from one machine, and every one of them can activate a grain before the session
    ///     has authenticated (SEC-10).
    ///
    ///     Sized for the case that is not an attack: a household, a school or a caf&#233; behind one
    ///     NAT address, where a dozen players legitimately share an IP, plus reconnect churn.
    /// </remarks>
    public int MaxSessionsPerIpAddress { get; init; } = 32;

    /// <summary>
    ///     Hard ceiling on concurrent sessions across every remote address. Zero disables it. The
    ///     last line before the process runs out of sockets; set it from what the host can carry.
    /// </summary>
    public int MaxTotalSessions { get; init; } = 5000;
}
