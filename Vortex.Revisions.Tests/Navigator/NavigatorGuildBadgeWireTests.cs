using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.NewNavigator;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Navigator;

/// <summary>
/// A navigator entry only advertises its guild when <see cref="RoomBitmaskFlags.GroupData"/> is
/// set, which the serializer derives from GroupId/GroupName/GroupBadge all being present.
/// <c>NavigatorService</c> rebuilds its result snapshots field by field and did not copy those
/// three across, so the flag was never set and no room ever showed a badge in the navigator.
/// </summary>
public sealed class NavigatorGuildBadgeWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void RoomWithGuild_SetsGroupDataFlagAndWritesTheBadge()
    {
        ClientPacket body = SerializeBlock(Room(groupId: 5, "Pixel Painters", "b0102"));

        RoomBitmaskFlags bitmask = ReadUpToBitmask(body);

        bitmask.HasFlag(RoomBitmaskFlags.GroupData).Should().BeTrue();
        body.PopInt().Should().Be(5);
        body.PopString().Should().Be("Pixel Painters");
        body.PopString().Should().Be("b0102");
        body.End.Should().BeTrue();
    }

    [Fact]
    public void RoomWithoutGuild_LeavesTheFlagClearAndWritesNothing()
    {
        ClientPacket body = SerializeBlock(Room(null, null, null));

        ReadUpToBitmask(body).HasFlag(RoomBitmaskFlags.GroupData).Should().BeFalse();
        body.End.Should().BeTrue();
    }

    /// <summary>A half-populated guild would desynchronise the packet, so all three are required.</summary>
    [Fact]
    public void RoomWithGuildIdButNoBadge_LeavesTheFlagClear()
    {
        ClientPacket body = SerializeBlock(Room(groupId: 5, "Pixel Painters", null));

        ReadUpToBitmask(body).HasFlag(RoomBitmaskFlags.GroupData).Should().BeFalse();
        body.End.Should().BeTrue();
    }

    private static RoomInfoSnapshot Room(int? groupId, string? groupName, string? groupBadge) =>
        new()
        {
            RoomId = 42,
            Name = "HQ",
            Description = "desc",
            OwnerId = (PlayerId)7,
            OwnerName = "absolutezeroo",
            Population = 0,
            DoorMode = RoomDoorModeType.Open,
            PlayersMax = 25,
            TradeType = RoomTradeModeType.Disabled,
            Score = 0,
            Ranking = 0,
            CategoryId = -1,
            Tags = [],
            AllowBlocking = false,
            AllowPets = false,
            AllowPetsEat = false,
            GroupId = groupId,
            GroupName = groupName,
            GroupBadge = groupBadge,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            LastUpdatedUtc = DateTime.UnixEpoch,
        };

    private static ClientPacket SerializeBlock(RoomInfoSnapshot room)
    {
        byte[] bytes = Revision
            .Serializers[typeof(NavigatorSearchResultBlocksMessageComposer)]
            .Serialize(
                new NavigatorSearchResultBlocksMessageComposer
                {
                    SearchCodeOriginal = "groups",
                    FilteringData = string.Empty,
                    Blocks =
                    [
                        new NavigatorSearchResultBlockSnapshot
                        {
                            SearchCode = "groups",
                            Text = string.Empty,
                            ActionAllowed = NavigatorActionAllowedType.Back,
                            Localization = string.Empty,
                            ForceClosed = false,
                            ViewMode = NavigatorViewModeType.Rows,
                            Results = [room],
                        },
                    ],
                }
            )
            .ToArray();

        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        return new ClientPacket(0, payload);
    }

    /// <summary>Consumes the block header and the room's fixed prefix, returning the bitmask.</summary>
    private static RoomBitmaskFlags ReadUpToBitmask(ClientPacket body)
    {
        body.PopString(); // searchCodeOriginal
        body.PopString(); // filteringData
        body.PopInt(); // block count
        body.PopString(); // block searchCode
        body.PopString(); // block text
        body.PopInt(); // actionAllowed
        body.PopBoolean(); // forceClosed
        body.PopInt(); // viewMode
        body.PopInt(); // result count

        body.PopInt(); // roomId
        body.PopString(); // name
        body.PopInt(); // ownerId
        body.PopString(); // ownerName
        body.PopInt(); // doorMode
        body.PopInt(); // population
        body.PopInt(); // playersMax
        body.PopString(); // description
        body.PopInt(); // tradeType
        body.PopInt(); // score
        body.PopInt(); // ranking
        body.PopInt(); // categoryId

        int tagCount = body.PopInt();
        for (int i = 0; i < tagCount; i++)
        {
            body.PopString();
        }

        return (RoomBitmaskFlags)body.PopInt();
    }
}
