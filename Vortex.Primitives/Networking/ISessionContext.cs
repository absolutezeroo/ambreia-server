using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Crypto;

namespace Vortex.Primitives.Networking;

public interface ISessionContext
{
    public SessionKey SessionKey { get; }
    public string RevisionId { get; }
    public DateTime LastActivityUtc { get; }
    public CancellationTokenSource HeartbeatCts { get; }
    public string? RemoteIpAddress { get; }
    public IRc4Engine? CryptoIn { get; }
    public IRc4Engine? CryptoOut { get; }
    public ArrayBufferWriter<byte>? WsBuffer { get; }
    public Task CloseSessionAsync();
    public void Touch();

    /// <summary>
    /// Counts one packet this connection sent that could not be parsed, and answers how many it has
    /// sent in total.
    /// </summary>
    /// <remarks>
    /// Parsing happens before the pipeline that holds the per-player quota, so a sender sending
    /// rubbish made the server look up a handler, attempt a parse and write a log line for every
    /// one of them without ever reaching that quota. A client that agrees with the server produces
    /// none of these, so a lifetime budget costs an honest connection nothing and bounds a
    /// dishonest one.
    /// </remarks>
    public int RecordParseFailure();
    public void SetRevisionId(string revisionId);
    public void SetupEncryption(byte[] key, bool setCryptoOut = false);
    public Task SendComposerAsync(IComposer composer, CancellationToken ct);
}
