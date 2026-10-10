using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuperSocket.ProtoBase;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Session;
using SuperSocket.WebSocket.Server;
using Vortex.Crypto;
using Vortex.Networking.Configuration;
using Vortex.Networking.Session;
using Vortex.Primitives.Crypto;
using Vortex.Primitives.Networking;

namespace Vortex.Networking.Ws;

/// <summary>
///     WebSocket-backed session context used by the shared packet/session pipeline.
/// </summary>
public sealed class WebSocketSessionContext(
    WebSocketSession session,
    IPackageEncoder<OutgoingPackage> packageEncoder,
    ILogger<WebSocketSessionContext> logger,
    NetworkingConfig networking
) : ISessionContext, IDisposable
{
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

    public SessionKey SessionKey { get; } = session.SessionID;
    public string RevisionId { get; private set; } = "Default";
    public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;
    public CancellationTokenSource HeartbeatCts { get; } = new();

    private readonly IReadOnlySet<IPAddress> _trustedProxies = ClientAddress.ParseTrusted(
        networking.TrustedProxies
    );

    // The client's address, not the proxy's, when a listed proxy spoke PROXY protocol or set
    // X-Forwarded-For on the handshake (audit P0-4). Resolved on each read, like the TCP session's:
    // the handshake header is read when it is asked for, not frozen at a moment it might not be there.
    public string? RemoteIpAddress =>
        ClientAddress.Resolve(
            (session.RemoteEndPoint as IPEndPoint)?.Address,
            ((IAppSession)session).Connection?.ProxyInfo?.SourceIPAddress,
            session.HttpHeader?.Items["X-Forwarded-For"],
            _trustedProxies
        );

    public IRc4Engine? CryptoIn { get; private set; }
    public IRc4Engine? CryptoOut { get; private set; }
    public ArrayBufferWriter<byte>? WsBuffer { get; } = new(4096);

    /// <summary>
    /// False once a send has found the transport gone.
    /// <para>
    /// <see cref="SessionState"/> is not enough on its own: the connection is torn down in stages,
    /// and there is a window where the pipe behind it has been completed while the session still
    /// reports itself open. A write in that window throws rather than returning, which is the whole
    /// reason this exists — a caller that repeats on a timer would otherwise keep discovering the
    /// same closed connection, one exception at a time, until the framework gets round to raising
    /// its closed event.
    /// </para>
    /// </summary>
    public bool IsConnected { get; private set; } = true;

    public async Task CloseSessionAsync()
    {
        await session.CloseAsync().ConfigureAwait(false);
    }

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
        if (ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _sendSemaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsGone(ex))
        {
            // Closing the session disposes this, and it happens while sends may still be queued
            // behind it. Waiting on a disposed semaphore throws from outside the block below, so
            // without this the caller sees an exception rather than a send that quietly went
            // nowhere -- which for the heartbeat meant a warning on an ordinary disconnect.
            IsConnected = false;

            return;
        }

        try
        {
            if (session.State == SessionState.Closed)
            {
                IsConnected = false;

                return;
            }

            ArrayBufferWriter<byte> writer = new(4096);
            int bytesWritten = packageEncoder.Encode(writer, new OutgoingPackage(this, composer));

            if (bytesWritten <= 0)
            {
                return;
            }

            byte[] payload = writer.WrittenSpan.ToArray();

            await session.SendAsync(payload).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            logger.LogDebug(
                ex,
                "Cancelled sending composer {ComposerType} to websocket session {SessionKey}",
                composer?.GetType().Name ?? "<null>",
                SessionKey
            );
        }
        catch (Exception ex) when (IsGone(ex))
        {
            // Not a failure worth a stack trace: the client left while this was being written. The
            // framework raises its closed event a moment later and everything is cleaned up then —
            // all this has to do is stop pretending the connection is still there.
            IsConnected = false;

            logger.LogDebug(
                "Dropped composer {ComposerType}: websocket session {SessionKey} is gone ({Reason}).",
                composer?.GetType().Name ?? "<null>",
                SessionKey,
                ex.GetType().Name
            );
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Failed to send composer {ComposerType} to websocket session {SessionKey}",
                composer?.GetType().Name ?? "<null>",
                SessionKey
            );
        }
        finally
        {
            try
            {
                _sendSemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // The session was closed while this send held the lock. There is nothing left to
                // release it for, and throwing from a finally would replace whatever the block was
                // already reporting.
                IsConnected = false;
            }
        }
    }

    /// <summary>
    /// Whether an exception means "there is no connection left", as opposed to something that went
    /// wrong on a connection that is still there.
    /// </summary>
    /// <remarks>
    /// The three shapes a departing client produces. <see cref="InvalidOperationException"/> is the
    /// literal one — SuperSocket writes into a pipe whose writer has been completed and the pipe
    /// says so — and it is matched on type alone rather than on its message, which is not ours to
    /// depend on. The cancellation is the send being cut off mid-flight by the framework's own
    /// token, which is why it is caught here rather than by the filter above: that one only fires
    /// for <em>our</em> token.
    /// </remarks>
    private static bool IsGone(Exception ex) =>
        ex is InvalidOperationException or ObjectDisposedException or OperationCanceledException;

    public void Dispose()
    {
        HeartbeatCts.Dispose();
        _sendSemaphore.Dispose();
    }
}
