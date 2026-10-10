using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuperSocket.Connection;
using SuperSocket.ProtoBase;
using SuperSocket.Server;
using Vortex.Crypto;
using Vortex.Networking.Configuration;
using Vortex.Primitives.Crypto;
using Vortex.Primitives.Networking;
using Vortex.Runtime;

namespace Vortex.Networking.Session;

public class SessionContext(
    IPackageEncoder<OutgoingPackage> packageEncoder,
    ILogger<SessionContext> logger,
    NetworkingConfig networking
) : AppSession(), ISessionContext
{
    private readonly IPackageEncoder<OutgoingPackage> _packageEncoder = packageEncoder;
    private readonly ILogger<SessionContext> _logger = logger;
    private readonly IReadOnlySet<IPAddress> _trustedProxies = ClientAddress.ParseTrusted(
        networking.TrustedProxies
    );
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

    public SessionKey SessionKey { get; private set; } = string.Empty;
    public string RevisionId { get; private set; } = "Default";
    public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;
    public CancellationTokenSource HeartbeatCts { get; } = new();

    // The client's address, not the proxy's, when a listed proxy spoke PROXY protocol (audit P0-4).
    public string? RemoteIpAddress =>
        ClientAddress.Resolve(
            (Connection?.RemoteEndPoint as IPEndPoint)?.Address,
            Connection?.ProxyInfo?.SourceIPAddress,
            forwardedFor: null,
            _trustedProxies
        );
    public IRc4Engine? CryptoIn { get; private set; }
    public IRc4Engine? CryptoOut { get; private set; }

    public ArrayBufferWriter<byte>? WsBuffer { get; } = new(4096);

    protected override async ValueTask OnSessionConnectedAsync()
    {
        SessionKey = this.SessionID;

        await base.OnSessionConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Records why a session ended. The client only ever sees close code 1006 for an abnormal
    ///     drop, and 1006 is the same code whether the server let go, the reverse proxy reaped the
    ///     connection or the route died — so the browser console cannot name the layer and this was
    ///     the only place that can. <see cref="CloseReason" /> does: <c>RemoteClosing</c> means the
    ///     peer side (the browser, or Caddy/Traefik acting for it) closed, <c>TimeOut</c> means
    ///     SuperSocket's own idle reaper, <c>SocketError</c> means the transport failed underneath.
    ///     The silence figure alongside it says whether the heartbeat was still getting answers.
    /// </summary>
    protected override async ValueTask OnSessionClosedAsync(CloseEventArgs e)
    {
        _logger.LogInformation(
            "Session {SessionKey} closed: reason={Reason} silent for {SilenceSeconds:F1}s",
            SessionKey,
            e.Reason,
            (DateTime.UtcNow - LastActivityUtc).TotalSeconds
        );

        await base.OnSessionClosedAsync(e).ConfigureAwait(false);
    }

    public async Task CloseSessionAsync() => await this.CloseAsync().ConfigureAwait(false);

    private int _parseFailures;

    public int RecordParseFailure() => Interlocked.Increment(ref _parseFailures);

    public void Touch()
    {
        LastActivityUtc = DateTime.UtcNow;
    }

    public void SetRevisionId(string revisionId)
    {
        RevisionId = revisionId;
    }

    public void SetupEncryption(byte[] key, bool setCryptoOut = false)
    {
        CryptoIn = new Rc4Engine(key);

        if (setCryptoOut)
        {
            CryptoOut = new Rc4Engine(key);
        }
    }

    public async Task SendComposerAsync(IComposer composer, CancellationToken ct)
    {
        await _sendSemaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (Connection.IsClosed)
            {
                return;
            }

            await Connection
                .SendAsync(_packageEncoder, new OutgoingPackage(this, composer), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Warning, not debug. The benign case — the peer went away — is already returned on by
            // the IsClosed check above, so reaching here means a composer the server believed it had
            // sent never left the process. At debug that is invisible in a normal run, and "sent but
            // never arrived" then looks identical to "never sent": the two are indistinguishable
            // from the client side, and telling them apart is most of the work in any wire bug.
            _logger.LogWarning(
                ex,
                "Failed to send composer {ComposerType} to session {SessionKey}",
                composer?.GetType().Name ?? "<null>",
                SessionKey
            );
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }
}
