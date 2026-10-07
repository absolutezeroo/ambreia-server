using System;
using System.Buffers;
using System.IO;
using FluentAssertions;
using Vortex.Networking.Package;
using Vortex.Primitives.Networking;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.Networking;

/// <summary>
/// A frame whose declared body is shorter than the two-byte header used to pass the bounds check
/// and throw from PopShort, which the WebSocket handler does not catch: the session stayed open
/// with its buffer never cleared, re-throwing on every later frame.
/// </summary>
public sealed class PacketLengthTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ABodyShorterThanTheHeader_IsInvalidData(int length)
    {
        byte[] frame = [0, 0, 0, (byte)length, 0];

        Action read = () =>
        {
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(frame));
            new ClientPacketDecoder().TryRead(
                ref reader,
                FakeProxy.Create<ISessionContext>(_ => null)
            );
        };

        read.Should().Throw<InvalidDataException>();
    }
}
