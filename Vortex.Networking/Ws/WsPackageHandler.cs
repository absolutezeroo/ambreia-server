using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Session;
using SuperSocket.WebSocket;
using Vortex.Networking.Package;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;

namespace Vortex.Networking.Ws;

internal sealed class WsPackageHandler(
    IClientPacketDecoder decoder,
    PackageHandler inner,
    ILogger<WsPackageHandler>? logger = null
) : IPackageHandler<WebSocketPackage>
{
    private readonly IClientPacketDecoder _decoder = decoder;
    private readonly PackageHandler _inner = inner;
    private readonly ILogger<WsPackageHandler>? _logger = logger;

    /// <summary>
    ///     Standard handler for DI-based sessions.
    /// </summary>
    public async ValueTask Handle(
        IAppSession session,
        WebSocketPackage package,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(package);

        if (session is not ISessionContext ctx || package.OpCode != OpCode.Binary)
        {
            return;
        }

        await ProcessPackageAsync(ctx, package, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Handler for WebSocket transports that keep the Vortex session context separate
    ///     from the SuperSocket session object.
    /// </summary>
    public async ValueTask HandleManualAsync(
        ISessionContext ctx,
        WebSocketPackage package,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.OpCode != OpCode.Binary)
        {
            return;
        }

        await ProcessPackageAsync(ctx, package, ct).ConfigureAwait(false);
    }

    private async ValueTask ProcessPackageAsync(
        ISessionContext ctx,
        WebSocketPackage package,
        CancellationToken ct
    )
    {
        // Record activity on the raw frame, before decoding. The heartbeat closes sessions that go
        // silent, so "silent" has to mean "sent us nothing", not "sent us nothing we could parse" —
        // otherwise an unrecognised header would count as death. This is also what makes the PONG
        // reply register without PongMessageHandler needing to do anything.
        ctx.Touch();

        foreach (ReadOnlyMemory<byte> segment in package.Data)
        {
            ctx.WsBuffer?.Write(segment.Span);
        }

        if (ctx.WsBuffer is null)
        {
            return;
        }

        // Every whole packet in the buffer is read by offset and handed on; the buffer is compacted
        // ONCE at the end, not after each packet — a frame of ten thousand six-byte packets used to
        // copy the whole remainder ten thousand times. Nothing writes the buffer meanwhile: frames of
        // one session arrive one after the other, and the decoder copies what it reads.
        ReadOnlyMemory<byte> memory = ctx.WsBuffer.WrittenMemory;
        int consumed = 0;

        while (consumed < memory.Length)
        {
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(memory[consumed..]));

            IClientPacket? packet;

            try
            {
                packet = _decoder.TryRead(ref reader, ctx);
            }
            catch (InvalidDataException ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Closing websocket session {SessionKey} ({RemoteIpAddress}) after an invalid packet frame",
                    ctx.SessionKey,
                    ctx.RemoteIpAddress
                );

                ctx.WsBuffer.Clear();

                await ctx.CloseSessionAsync().ConfigureAwait(false);

                return;
            }

            if (packet is null)
            {
                break;
            }

            consumed += (int)reader.Consumed;

            await _inner.HandleAsync(ctx, packet, ct).ConfigureAwait(false);
        }

        if (consumed == 0)
        {
            return;
        }

        byte[] remaining = memory[consumed..].ToArray();

        ctx.WsBuffer.Clear();

        if (remaining.Length > 0)
        {
            ctx.WsBuffer.Write(remaining);
        }
    }
}
