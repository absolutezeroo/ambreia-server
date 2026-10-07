using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Furniture.Snapshots.StuffData;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Room;

/// <summary>
///     A wall item's data is one string on the wire, whatever its stuff-data type. WIN63's
///     parseItemData (unknowns/_SafePkg_2184/_SafeCls_4408.as) reads string/int/string/string then
///     three ints with no branch on the data type — unlike a floor item, which carries the full
///     polymorphic blob.
///
///     The serializer used to write that string only for a LegacyStuffSnapshot, so any other type
///     (a wall photo carries MapStuffData) dropped the field and the client read the expiration int
///     as a string length, shifting every later field in the packet.
/// </summary>
public sealed class WallItemStuffDataWireTests
{
    private static readonly Rev Revision = new();

    private static ClientPacket SerializeAndReadBody(ItemsMessageComposer composer)
    {
        byte[] bytes = Revision
            .Serializers[typeof(ItemsMessageComposer)]
            .Serialize(composer)
            .ToArray();
        // AbstractSerializer prepends int length (4) + short header (2).
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    private static RoomWallItemSnapshot WallItem(StuffDataSnapshot stuffData) =>
        new()
        {
            ObjectId = new RoomObjectId(4711),
            OwnerId = new PlayerId(9),
            OwnerName = "Owner",
            DefinitionId = 1,
            SpriteId = 4242,
            X = 1,
            Y = 2,
            Z = default,
            Rotation = Rotation.South,
            StackHeight = default,
            StuffData = stuffData,
            ExtraData = string.Empty,
            UsagePolicy = default,
            WallOffset = 0,
            WallPosition = ":w=1,2 l=0,0 l",
        };

    private static ItemsMessageComposer Compose(StuffDataSnapshot stuffData) =>
        new()
        {
            OwnerNames = ImmutableDictionary<PlayerId, string>.Empty.Add(new PlayerId(9), "Owner"),
            WallItems = ImmutableArray.Create(WallItem(stuffData)),
        };

    /// <summary>Reads the packet exactly as the client's parseItemData does.</summary>
    private static (
        string Id,
        int SpriteId,
        string Position,
        string Data,
        int Expiration,
        int Usage,
        int OwnerId
    ) ReadItem(ClientPacket packet)
    {
        packet.PopInt().Should().Be(1, "one owner name precedes the items");
        packet.PopInt();
        packet.PopString();
        packet.PopInt().Should().Be(1, "one wall item follows");

        return (
            packet.PopString(),
            packet.PopInt(),
            packet.PopString(),
            packet.PopString(),
            packet.PopInt(),
            packet.PopInt(),
            packet.PopInt()
        );
    }

    [Fact]
    public void LegacyStuffData_WritesItsString()
    {
        ClientPacket packet = SerializeAndReadBody(
            Compose(new LegacyStuffSnapshot { StuffBitmask = 0, Data = "1" })
        );

        (
            string Id,
            int SpriteId,
            string Position,
            string Data,
            int Expiration,
            int Usage,
            int OwnerId
        ) item = ReadItem(packet);

        item.Id.Should().Be("4711");
        item.SpriteId.Should().Be(4242);
        item.Position.Should().Be(":w=1,2 l=0,0 l");
        item.Data.Should().Be("1");
        item.Expiration.Should().Be(-1);
        item.OwnerId.Should().Be(9);
        packet.Remaining.Should().Be(0, "the client consumes the packet exactly");
    }

    [Fact]
    public void NonLegacyStuffData_StillWritesTheStringField()
    {
        // A wall photo. Before the fix nothing was written here and the three trailing ints slid
        // into the wrong reads.
        ClientPacket packet = SerializeAndReadBody(
            Compose(
                new MapStuffSnapshot
                {
                    StuffBitmask = 5,
                    Data = ImmutableDictionary<string, string>.Empty.Add("photo", "1234"),
                }
            )
        );

        (
            string Id,
            int SpriteId,
            string Position,
            string Data,
            int Expiration,
            int Usage,
            int OwnerId
        ) item = ReadItem(packet);

        item.Id.Should().Be("4711");
        item.SpriteId.Should().Be(4242);
        item.Position.Should().Be(":w=1,2 l=0,0 l");
        item.Data.Should()
            .BeEmpty("non-legacy wall data has no legacy projection on the snapshot yet");
        item.Expiration.Should()
            .Be(-1, "the field after the data string must still land on the expiration");
        item.OwnerId.Should().Be(9);
        packet.Remaining.Should().Be(0, "the client consumes the packet exactly");
    }
}
