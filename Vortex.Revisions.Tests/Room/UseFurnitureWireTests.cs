using System;
using System.Buffers.Binary;
using FluentAssertions;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Room.Engine;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Room;

/// <summary>
/// Using a furni is (objectId, param) on 3353 for a floor item and 3590 for a wall item, as the
/// WIN63 room engine sends them; without these parsers a double click did nothing at all.
/// </summary>
public sealed class UseFurnitureWireTests
{
    private static readonly Rev Revision = new();

    private static ClientPacket Packet(int header, int objectId, int param)
    {
        byte[] body = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(body, objectId);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(4), param);

        return new ClientPacket(header, body);
    }

    [Fact]
    public void UseFurniture_ReadsTheObjectAndTheParam()
    {
        UseFurnitureMessage message = (UseFurnitureMessage)
            Revision.Parsers[3353].Parse(Packet(3353, 12, 0));

        (message.ObjectId.Value, message.Param).Should().Be((12, 0));
    }

    [Fact]
    public void UseWallItem_ReadsTheObjectAndTheParam()
    {
        UseWallItemMessage message = (UseWallItemMessage)
            Revision.Parsers[3590].Parse(Packet(3590, 40, 1));

        (message.ObjectId.Value, message.Param).Should().Be((40, 1));
    }
}
