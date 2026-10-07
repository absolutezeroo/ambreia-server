using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Commerce;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Loot;

/// <summary>
/// The payout half of a prize that cost a furniture: a crackable, a mystery trophy, a mystery box.
/// All three destroy the item before granting -- the reverse order lets a repeated click mint prizes
/// -- so from the consume onwards the prize is owed, and it used to be owed with nothing anywhere
/// recording that it was (RSYS-PRIZE-050).
/// </summary>
public sealed class ConsumedLootJournalTests
{
    [Fact]
    public async Task ALootThatLands_IsPivotedThenCompleted()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.GrantConsumedLootAsync(
                RoomHarness.Stranger,
                "crackable=1",
                _ => Task.CompletedTask,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness
            .JournalStates.Should()
            .Equal(CommerceOperationState.Pivoted, CommerceOperationState.Completed);
    }

    [Fact]
    public async Task ALootThatCannotBeHandedOver_GoesOnTheOperatorsList()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        Func<Task> grant = () =>
            harness.Grain.GrantConsumedLootAsync(
                RoomHarness.Stranger,
                "crackable=1",
                _ => throw new InvalidOperationException("prize grain unavailable"),
                CancellationToken.None
            );

        // Rethrown, because three callers already behave a particular way when a payout throws and
        // this exists to record what happened rather than to change it.
        await grant.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        harness
            .JournalStates.Should()
            .Equal(CommerceOperationState.Pivoted, CommerceOperationState.NeedsIntervention);
    }

    [Fact]
    public async Task AJournalThatIsDown_DoesNotCostThePlayerTheirLoot()
    {
        // The furniture is already gone. Refusing to pay out because the bookkeeping failed turns a
        // lost note into a lost prize.
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        harness.JournalThrows = true;

        bool granted = false;

        await harness
            .Grain.GrantConsumedLootAsync(
                RoomHarness.Stranger,
                "crackable=1",
                _ =>
                {
                    granted = true;

                    return Task.CompletedTask;
                },
                CancellationToken.None
            )
            .ConfigureAwait(true);

        granted.Should().BeTrue();
    }
}
