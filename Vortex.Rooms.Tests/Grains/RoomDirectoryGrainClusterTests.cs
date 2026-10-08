using System;
using System.Threading;
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
/// calls are exercised the way production hosting exercises them. It counts who stands in each
/// active room, which is what keeps an occupied room up.
/// </summary>
[Collection(VortexClusterCollection.Name)]
public sealed class RoomDirectoryGrainClusterTests(VortexClusterFixture cluster)
{
    private readonly VortexClusterFixture _cluster = cluster;

    [Fact]
    public async Task APlayerAddedToAnActiveRoom_IsCounted()
    {
        IRoomDirectoryGrain grain = _cluster.GrainFactory.GetGrain<IRoomDirectoryGrain>(
            "cluster-poc-directory"
        );
        RoomId roomId = 42;

        await grain.UpsertActiveRoomAsync(Snapshot(roomId)).ConfigureAwait(true);
        await grain
            .AddPlayerToRoomAsync((PlayerId)7, roomId, CancellationToken.None)
            .ConfigureAwait(true);

        (await grain.GetRoomPopulationAsync(roomId).ConfigureAwait(true)).Should().Be(1);
    }

    [Fact]
    public async Task RemovingAnActiveRoom_DropsItsOccupants()
    {
        IRoomDirectoryGrain grain = _cluster.GrainFactory.GetGrain<IRoomDirectoryGrain>(
            "cluster-poc-directory-remove"
        );
        RoomId roomId = 43;

        await grain.UpsertActiveRoomAsync(Snapshot(roomId)).ConfigureAwait(true);
        await grain
            .AddPlayerToRoomAsync((PlayerId)7, roomId, CancellationToken.None)
            .ConfigureAwait(true);
        await grain.RemoveActiveRoomAsync(roomId).ConfigureAwait(true);

        (await grain.GetRoomPopulationAsync(roomId).ConfigureAwait(true)).Should().Be(0);
    }

    private static RoomInfoSnapshot Snapshot(RoomId roomId) =>
        new()
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
}
