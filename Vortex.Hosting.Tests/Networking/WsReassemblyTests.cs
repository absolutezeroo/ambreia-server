using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SuperSocket.WebSocket;
using Vortex.Messages;
using Vortex.Messages.RateLimit;
using Vortex.Networking.Configuration;
using Vortex.Networking.Package;
using Vortex.Networking.Ws;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Primitives.Observability;
using Vortex.Revisions.Revision20260701;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.Networking;

/// <summary>
/// A WebSocket frame carries any number of game packets, whole or cut: every whole one is
/// delivered in order, the cut one waits for its tail. The buffer is compacted once per frame, not
/// once per packet, so a frame of many small packets costs what it weighs.
/// </summary>
public sealed class WsReassemblyTests
{
    // GetTalentTrackMessageEvent: no parser, so delivery is observed on the session's token bucket
    // (one token per unknown header) without reaching a message handler.
    private const int UNKNOWN_HEADER = 3757;

    private static readonly Revision20260701 Revision = new();

    [Fact]
    public async Task AFrameOfWholePacketsAndACutOne_DeliversTheWholeOnes_AndKeepsTheCut()
    {
        int delivered = 0;
        WsPackageHandler handler = Handler(() => delivered++);
        Session session = new();
        byte[] packet = Packet(UNKNOWN_HEADER, bodyLength: 4);
        byte[] frame = [.. packet, .. packet, .. packet, .. packet.AsSpan(0, 5)];

        await handler.HandleManualAsync(session.Context, Frame(frame), default);

        delivered.Should().Be(3);
        session.Context.WsBuffer!.WrittenCount.Should().Be(5, "the cut packet waits for its tail");

        await handler.HandleManualAsync(
            session.Context,
            Frame(packet.AsSpan(5).ToArray()),
            default
        );

        delivered.Should().Be(4);
        session.Context.WsBuffer!.WrittenCount.Should().Be(0);
    }

    [Fact]
    public async Task AFrameOfTenThousandSmallPackets_DeliversThemAll()
    {
        int delivered = 0;
        WsPackageHandler handler = Handler(() => delivered++);
        Session session = new();
        byte[] packet = Packet(UNKNOWN_HEADER, bodyLength: 0);
        byte[] frame = new byte[packet.Length * 10_000];

        for (int i = 0; i < 10_000; i++)
        {
            packet.CopyTo(frame, i * packet.Length);
        }

        await handler.HandleManualAsync(session.Context, Frame(frame), default);

        delivered.Should().Be(10_000);
    }

    /// <summary>[int32 BE length][int16 BE header][body], length covering header and body.</summary>
    private static byte[] Packet(int header, int bodyLength)
    {
        byte[] bytes = new byte[4 + 2 + bodyLength];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 2 + bodyLength);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(4), (short)header);

        return bytes;
    }

    private static WebSocketPackage Frame(byte[] bytes) =>
        new() { OpCode = OpCode.Binary, Data = new ReadOnlySequence<byte>(bytes) };

    private static WsPackageHandler Handler(Action onDelivered) =>
        new(
            new ClientPacketDecoder(NetworkingConfig.DefaultMaxPacketBodyBytes),
            new PackageHandler(
                FakeProxy.Create<IRevisionManager>(call =>
                    call.Method.Name == nameof(IRevisionManager.GetRevision) ? Revision : null
                ),
                new MessageSystem(
                    null!,
                    null!,
                    null!,
                    null!,
                    null!,
                    NullLogger<MessageSystem>.Instance
                ),
                NullLogger<PackageHandler>.Instance,
                FakeProxy.Create<IVortexContextAccessor>(_ => null),
                FakeProxy.Create<IErrorGroupingSink>(_ => null),
                new NetworkingConfig(),
                FakeProxy.Create<IRateLimiter>(_ =>
                {
                    onDelivered();

                    return true;
                }),
                FakeProxy.Create<IVortexMetrics>(_ => null)
            ),
            NullLogger<WsPackageHandler>.Instance
        );

    /// <summary>A plaintext session with a real reassembly buffer and nothing else.</summary>
    private sealed class Session
    {
        private readonly ArrayBufferWriter<byte> _buffer = new(4096);

        public ISessionContext Context { get; }

        public Session() =>
            Context = FakeProxy.Create<ISessionContext>(call =>
                call.Method.Name switch
                {
                    "get_" + nameof(ISessionContext.WsBuffer) => _buffer,
                    "get_" + nameof(ISessionContext.RevisionId) => Revision.Revision,
                    "get_" + nameof(ISessionContext.SessionKey) => new SessionKey("ws"),
                    _ => null,
                }
            );
    }
}
