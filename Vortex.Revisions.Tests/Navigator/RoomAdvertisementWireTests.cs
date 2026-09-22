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
/// A room's advertisement travels behind bit 4 of the room bitmask, and the client leans on it hard:
/// in the event view mode it draws the room card's title from <c>roomAdName</c> rather than from the
/// room's name. The serializer used to declare the bit clear and write nothing, so every ad this
/// hotel had sold was invisible and the events tab rendered nameless rows.
/// </summary>
/// <remarks>
/// Layout taken from the target client's room-data constructor: name, description, then the
/// remaining minutes as an int.
/// </remarks>
public sealed class RoomAdvertisementWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void RoomWithARunningAd_SetsTheAdFlagAndWritesNameDescriptionAndMinutes()
    {
        ClientPacket body = SerializeBlock(
            Room() with
            {
                AdName = "Pool party",
                AdDescription = "Bring a towel",
                AdExpiresAt = DateTime.UtcNow.AddMinutes(30),
            }
        );

        RoomBitmaskFlags bitmask = ReadUpToBitmask(body);

        bitmask.HasFlag(RoomBitmaskFlags.RoomAd).Should().BeTrue();
        body.PopString().Should().Be("Pool party");
        body.PopString().Should().Be("Bring a towel");
        body.PopInt().Should().BeInRange(29, 30);
        body.End.Should().BeTrue();
    }

    [Fact]
    public void RoomWithoutAnAd_LeavesTheFlagClearAndWritesNothing()
    {
        ClientPacket body = SerializeBlock(Room());

        ReadUpToBitmask(body).HasFlag(RoomBitmaskFlags.RoomAd).Should().BeFalse();
        body.End.Should().BeTrue();
    }

    /// <summary>An ad that ran out between the query and the send counts down to nothing rather
    /// than to a negative number — the client reads a non-positive value as "no ad".</summary>
    [Fact]
    public void AnExpiredAd_CountsDownToZeroRatherThanBelowIt()
    {
        ClientPacket body = SerializeBlock(
            Room() with
            {
                AdName = "Over already",
                AdDescription = string.Empty,
                AdExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            }
        );

        ReadUpToBitmask(body).HasFlag(RoomBitmaskFlags.RoomAd).Should().BeTrue();
        body.PopString();
        body.PopString();
        body.PopInt().Should().Be(0);
    }

    /// <summary>
    /// The owner bit is what makes the client draw an owner line at all, and it never looks at the
    /// name behind it. Announcing an owner we cannot name renders that line empty.
    /// </summary>
    [Fact]
    public void RoomWithAnOwnerName_AnnouncesTheOwner()
    {
        ClientPacket body = SerializeBlock(Room() with { OwnerName = "absolutezeroo" });

        ReadUpToBitmask(body).HasFlag(RoomBitmaskFlags.ShowOwner).Should().BeTrue();
    }

    [Fact]
    public void RoomWithNoOwnerName_LeavesTheOwnerBitClear()
    {
        ClientPacket body = SerializeBlock(Room() with { OwnerName = string.Empty });

        ReadUpToBitmask(body).HasFlag(RoomBitmaskFlags.ShowOwner).Should().BeFalse();
    }

    /// <summary>
    /// Bit 32 is not "this room is advertised". The client answers it by calling out to Habbo's web
    /// ad slot, so a hotel that does not serve one must leave it clear whatever its ads say.
    /// </summary>
    [Fact]
    public void ARunningAd_DoesNotAskTheClientToFetchAWebInterstitial()
    {
        ClientPacket body = SerializeBlock(
            Room() with
            {
                AdName = "Pool party",
                AdExpiresAt = DateTime.UtcNow.AddMinutes(30),
            }
        );

        ReadUpToBitmask(body).HasFlag(RoomBitmaskFlags.DisplayRoomAd).Should().BeFalse();
    }

    private static RoomInfoSnapshot Room() =>
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
            StaffPick = false,
            AllowBlocking = false,
            AllowPets = false,
            AllowPetsEat = false,
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
                    SearchCodeOriginal = "roomads",
                    FilteringData = string.Empty,
                    Blocks =
                    [
                        new NavigatorSearchResultBlockSnapshot
                        {
                            SearchCode = "roomads",
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
