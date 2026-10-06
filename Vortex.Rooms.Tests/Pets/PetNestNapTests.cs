using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// A pet sent to its nest (command 13) naps there and gets back up on its own. It used to wake
/// once its energy had climbed back; energy no longer moves since M0, so without a fixed nap
/// length an ordered pet would sleep for good -- and a sleeping pet refuses every order.
/// </summary>
public sealed class PetNestNapTests
{
    private const int PetId = 31;
    private const int NestX = 3;
    private const int NestY = 3;

    [Fact]
    public async Task APetOrderedToItsNest_SleepsThenWakesAfterTheNap()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        harness.Grain._state.IsMapReady = true;
        harness.Grain._state.IsFurniLoaded = true;
        GameFurni.Place(
            harness,
            "pet_nest",
            NestX,
            NestY,
            (stuffData, ctx) => new FurniturePetNestLogic(stuffData, ctx)
        );
        await harness.PutPetInRoomAsync(PetId, NestX, NestY).ConfigureAwait(true);

        // Always obeys, so the order is the thing under test rather than the dice.
        harness.ConfigOverrides[PetTuning.CommandObeyBaseChancePercentKey] = 100;
        harness.ConfigOverrides[PetTuning.CommandObeyMaxChancePercentKey] = 100;
        harness.PetCommands = (type, id) =>
            id == RoomPetRuntime.NestCommandId
                ? new PetCommandEntry
                {
                    PetType = type,
                    CommandId = id,
                    LevelRequired = 0,
                    Posture = string.Empty,
                    EnergyCost = 0,
                    XpReward = 0,
                }
                : null;

        int napMs = new PetConfig().NestNapMs;
        long now = harness.Grain.NowMs();

        // One tick first: it loads the tuning and gives the pet its motion state.
        await TickAsync(harness, now).ConfigureAwait(true);

        PetSnapshot? obeyed = await harness
            .Grain.PetSystem.IssueCommandAsync(
                harness.ContextFor(RoomHarness.Owner),
                PetId,
                RoomPetRuntime.NestCommandId,
                CancellationToken.None
            )
            .ConfigureAwait(true);
        obeyed.Should().NotBeNull("the owner ordered it");

        await TickAsync(harness, now + 1_000).ConfigureAwait(true);
        SleepStates(harness).Should().Equal([true], "already on the nest, it lies down at once");

        await TickAsync(harness, now + (napMs / 2)).ConfigureAwait(true);
        SleepStates(harness).Should().Equal([true], "half-way through the nap it still sleeps");

        await TickAsync(harness, now + 1_000 + napMs + 1_000).ConfigureAwait(true);
        SleepStates(harness).Should().Equal([true, false], "the nap is over");
    }

    private static Task TickAsync(RoomHarness harness, long now) =>
        harness.Grain.PetSystem.ProcessPetsAsync(now, CancellationToken.None);

    private static bool[] SleepStates(RoomHarness harness) =>
        [
            .. harness
                .BroadcastToRoom.OfType<SleepMessageComposer>()
                .Select(sleep => sleep.IsSleeping),
        ];
}
