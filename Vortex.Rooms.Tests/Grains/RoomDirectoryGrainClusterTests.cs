using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Xunit;

namespace Vortex.Rooms.Tests.Grains;

/// <summary>
/// The directory grain end-to-end inside a real silo, so activation, DI wiring, and grain-reference
/// calls are exercised the way production hosting exercises them (as opposed to the
/// hand-constructed grain tests in Vortex.Rooms.Tests/Groups). It was the first grain covered this
/// way because it has no database dependency.
/// </summary>
[Collection(VortexClusterCollection.Name)]
public sealed class RoomDirectoryGrainClusterTests(VortexClusterFixture cluster)
{
    private readonly VortexClusterFixture _cluster = cluster;

    [Fact]
    public async Task UpsertActiveRoomAsync_ThenGetActiveRoomsAsync_ReturnsUpsertedRoom()
    {
        IRoomDirectoryGrain grain = _cluster.GrainFactory.GetGrain<IRoomDirectoryGrain>(
            "cluster-poc-directory"
        );
        RoomId roomId = 42;
        RoomInfoSnapshot snapshot = new RoomInfoSnapshot
        {
            RoomId = roomId,
            Name = "Cluster Test Room",
            Description = "poc",
            OwnerId = (PlayerId)1,
            OwnerName = "Owner",
            Population = 0,
            LastUpdatedUtc = DateTime.UtcNow,
            DoorMode = RoomDoorModeType.Open,
            PlayersMax = 25,
            TradeType = RoomTradeModeType.Disabled,
            Ranking = 0,
            CategoryId = -1,
            Tags = [],
            AllowBlocking = false,
            AllowPets = false,
            AllowPetsEat = false,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
        };

        await grain.UpsertActiveRoomAsync(snapshot).ConfigureAwait(true);
        ImmutableArray<RoomSummarySnapshot> rooms = await grain
            .GetActiveRoomsAsync()
            .ConfigureAwait(true);

        rooms.Should().ContainSingle(r => r.RoomId == roomId && r.Name == "Cluster Test Room");
    }

    [Fact]
    public async Task RemoveActiveRoomAsync_RemovesPreviouslyUpsertedRoom()
    {
        IRoomDirectoryGrain grain = _cluster.GrainFactory.GetGrain<IRoomDirectoryGrain>(
            "cluster-poc-directory-remove"
        );
        RoomId roomId = 43;
        RoomInfoSnapshot snapshot = new RoomInfoSnapshot
        {
            RoomId = roomId,
            Name = "Removable Room",
            Description = "poc",
            OwnerId = (PlayerId)1,
            OwnerName = "Owner",
            Population = 0,
            LastUpdatedUtc = DateTime.UtcNow,
            DoorMode = RoomDoorModeType.Open,
            PlayersMax = 25,
            TradeType = RoomTradeModeType.Disabled,
            Ranking = 0,
            CategoryId = -1,
            Tags = [],
            AllowBlocking = false,
            AllowPets = false,
            AllowPetsEat = false,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
        };

        await grain.UpsertActiveRoomAsync(snapshot).ConfigureAwait(true);
        await grain.RemoveActiveRoomAsync(roomId).ConfigureAwait(true);
        ImmutableArray<RoomSummarySnapshot> rooms = await grain
            .GetActiveRoomsAsync()
            .ConfigureAwait(true);

        rooms.Should().BeEmpty();
    }
}
