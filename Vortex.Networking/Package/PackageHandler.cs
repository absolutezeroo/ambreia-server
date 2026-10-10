using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Session;
using Vortex.Messages;
using Vortex.Messages.RateLimit;
using Vortex.Networking.Configuration;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Packets;

namespace Vortex.Networking.Package;

public sealed class PackageHandler(
    IRevisionManager revisionManager,
    MessageSystem messageSystem,
    ILogger<PackageHandler> logger,
    IVortexContextAccessor contextAccessor,
    IErrorGroupingSink errorSink,
    // The resolved config, not IOptions: this type is also built inside the TCP host's own child
    // container, which only gets the singletons NetworkManager forwards into it by hand.
    NetworkingConfig networking,
    // The session's own packet bucket (the one RateLimitBehavior spends after a parse): an unknown
    // header spends a token too, so a sender of rubbish pays for it before authentication.
    IRateLimiter limiter,
    IVortexMetrics metrics
) : IPackageHandler<IClientPacket>
{
    private const string PAYLOAD_REDACTED = "<redacted: Vortex:Networking:LogPacketPayloadOnError>";

    private readonly IVortexContextAccessor _contextAccessor = contextAccessor;
    private readonly IErrorGroupingSink _errorSink = errorSink;
    private readonly ILogger<PackageHandler> _logger = logger;
    private readonly MessageSystem _messageSystem = messageSystem;
    private readonly IRevisionManager _revisionManager = revisionManager;
    private readonly NetworkingConfig _networking = networking;
    private readonly IRateLimiter _limiter = limiter;
    private readonly IVortexMetrics _metrics = metrics;

    public ValueTask Handle(IAppSession session, IClientPacket packet, CancellationToken ct)
    {
        return HandleCoreAsync((ISessionContext)session, packet, ct);
    }

    /// <summary>
    ///     Direct handler for transports whose session context is managed outside SuperSocket.
    /// </summary>
    public ValueTask HandleAsync(ISessionContext ctx, IClientPacket packet, CancellationToken ct)
    {
        return HandleCoreAsync(ctx, packet, ct);
    }

    public async ValueTask HandleCoreAsync(
        ISessionContext ctx,
        IClientPacket packet,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(packet);

        try
        {
            IRevision revision =
                _revisionManager.GetRevision(ctx.RevisionId)
                ?? throw new InvalidOperationException(
                    $"No revision registered for revision id '{ctx.RevisionId}'."
                );

            if (revision.Parsers.TryGetValue(packet.Header, out IParser? parser))
            {
                IMessageEvent message = parser.Parse(packet);

                _logger.LogDebug("Incoming {MessageType}", message.GetType().Name);

                await _messageSystem.PublishAsync(message, ctx, ct).ConfigureAwait(false);
            }
            else if (_limiter.TryConsume(ctx.SessionKey))
            {
                // A header this revision does not parse: dropped, and said once per token, so a
                // client sending a removed feature's packets is heard and a flood is not.
                _logger.LogWarning(
                    "Incoming Unknown {Header} for {SessionKey}",
                    packet.Header,
                    ctx.SessionKey
                );
            }
            else
            {
                // Out of tokens: counted like a parse failure, no log line. A frame of ten thousand
                // unknown headers is one closed connection (SEC: cost bounded per byte, pre-auth).
                _metrics.PacketDropped("unknown_header");

                await CloseIfOverTheParseBudgetAsync(ctx).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // The header, length and read position always: that is what says which parser
            // disagreed with the client and where. The bytes only when asked for, because this
            // same line dumps the SSO packet, whose first field is the ticket -- send it, omit the
            // int behind it, and the parser throws with the credential at offset 0 of what gets
            // written down. The 128-byte cap bounds the volume, not the secret.
            _logger.LogError(
                ex,
                "Failed to process packet {Packet} for session {SessionKey} at byte {Position} of {Length} ({Remaining} left): {Payload}",
                packet.Header,
                ctx.SessionKey,
                packet.Position,
                packet.Position + packet.Remaining,
                packet.Remaining,
                _networking.LogPacketPayloadOnError ? packet.ToHexDump() : PAYLOAD_REDACTED
            );

            // Admission, after the fact but before the next one. Parsing runs ahead of the pipeline
            // that holds the per-player quota, so a sender sending rubbish made the server resolve a
            // handler, attempt a parse and write a log line for each packet without ever reaching
            // it. A client that agrees with this revision produces none of these, so the budget
            // costs an honest connection nothing.
            await CloseIfOverTheParseBudgetAsync(ctx).ConfigureAwait(false);

            IVortexContext? context = _contextAccessor.Current;

            _errorSink.Record(
                ex,
                "package-handler",
                $"packet:{packet.Header}",
                context?.PlayerId,
                context?.RoomId,
                context?.CorrelationId.Value,
                context?.SessionKey,
                ctx.RemoteIpAddress
            );
        }
    }

    /// <summary>
    /// One more packet the session should not have sent; the budget's last one closes it. Exactly
    /// the last one: the frame that crossed the line may still carry thousands more, and each must
    /// cost a counter increment, not a log line and a second close. A close that throws is not
    /// retried by the next packet (the count is past the line by then): the transport's own close
    /// path is what ends such a session.
    /// </summary>
    private async Task CloseIfOverTheParseBudgetAsync(ISessionContext ctx)
    {
        int failures = ctx.RecordParseFailure();

        if (
            _networking.MaxParseFailuresPerSession > 0
            && failures == _networking.MaxParseFailuresPerSession
        )
        {
            _logger.LogWarning(
                "Session {SessionKey} sent {Failures} packets that could not be parsed or were out of budget; closing it.",
                ctx.SessionKey,
                failures
            );

            await ctx.CloseSessionAsync().ConfigureAwait(false);
        }
    }
}
