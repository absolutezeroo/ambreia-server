using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Inventory.Furni;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Room;

/// <summary>
/// Applying a wallpaper, floor or landscape. The client sends RequestRoomPropertySet (2292,
/// WIN63 <c>_SafeCls_2323(int)</c>) with the inventory ITEM id — the parser used to name it RoomId,
/// and the handler looked up a room with it.
/// </summary>
public sealed class RoomDecorationWireTests
{
    private const int RequestRoomPropertySet = 2292;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void RequestRoomPropertySet_ReadsTheInventoryItemId()
    {
        ServerPacket sp = new(RequestRoomPropertySet);
        sp.WriteInteger(4815);

        ClientPacket packet = new(RequestRoomPropertySet, sp.ToArray());

        RequestRoomPropertySetMessage message = (RequestRoomPropertySetMessage)
            Revision.Parsers[RequestRoomPropertySet].Parse(packet);

        message.ItemId.Should().Be(4815);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void RoomProperty_WritesTheKeyThenTheCode()
    {
        IComposer composer = new RoomPropertyMessageComposer { Key = "landscape", Value = "1.10" };

        byte[] bytes = Revision.Serializers[composer.GetType()].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        ClientPacket packet = new(0, body);

        packet.PopString().Should().Be("landscape");
        packet.PopString().Should().Be("1.10");
        packet.Remaining.Should().Be(0);
    }
}
