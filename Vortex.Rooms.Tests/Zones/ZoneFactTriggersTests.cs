using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Spec Z1 §5.2–5.3: a level reached in the zone fires the PlayerLevel triggers aimed at it, each
/// level crossed; an item whose count goes up fires the ItemOwned triggers aimed at it. What the
/// entry reads fires nothing: it has to happen here.
/// </summary>
public sealed class ZoneFactTriggersTests
{
    private static ZoneTrigger On(ZoneTriggerType type, string target, string says) =>
        new()
        {
            Id = $"t_{type}_{target}",
            Type = type,
            Target = target,
            Actions = [new ShowDialogueAction(says)],
        };

    private static ZoneDocument Zone() =>
        ZoneSamples.Minimal() with
        {
            Triggers =
            [
                On(ZoneTriggerType.PlayerLevel, "3", "lvl3"),
                On(ZoneTriggerType.ItemOwned, "item.key", "key"),
            ],
        };

    private static long _sequence;

    /// <summary>The character's part, each one newer than the last.</summary>
    private static CharacterZoneFacts Facts(int level, int keys = 0) =>
        new()
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Level = level,
            Items =
                keys > 0
                    ? ImmutableDictionary<string, int>.Empty.Add("item.key", keys)
                    : ImmutableDictionary<string, int>.Empty,
        };

    private static string[] Bubbles(RoomHarness harness) =>
        [
            .. Sent<NotificationDialogMessageComposer>(harness)
                .Select(n => n.Parameters.Single(p => p.Key == "message").Value),
        ];

    private static async Task<RoomHarness> InsideAsync(CharacterZoneFacts facts)
    {
        RoomHarness harness = await ZoneAsync(Zone());
        harness.Facts[Walker] = facts;
        await EnterAsync(harness);

        return harness;
    }

    [Fact]
    public async Task ReachingTheLevel_FiresItsTrigger()
    {
        RoomHarness harness = await InsideAsync(Facts(2));

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Facts(3));

        Bubbles(harness).Should().Equal("lvl3");
    }

    [Fact]
    public async Task SkippingLevels_FiresEachOneCrossed()
    {
        RoomHarness harness = await InsideAsync(Facts(1));

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Facts(4));

        Bubbles(harness).Should().Equal("lvl3");
    }

    [Fact]
    public async Task TheReadingOnEntry_FiresNothing()
    {
        RoomHarness harness = await InsideAsync(Facts(3, keys: 1));

        Bubbles(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task GainingTheItem_FiresItsTrigger()
    {
        RoomHarness harness = await InsideAsync(Facts(1));

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Facts(1, keys: 1));

        Bubbles(harness).Should().Equal("key");
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public async Task LosingOrKeepingTheItem_FiresNothing(int before, int after)
    {
        RoomHarness harness = await InsideAsync(Facts(1, keys: before));

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Facts(1, keys: after));

        Bubbles(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task AFirstUpdate_WithoutAReadingOnEntry_FiresNothing()
    {
        // The entry read failed or found nothing yet: there is no "before" to compare with.
        RoomHarness harness = await ZoneAsync(Zone());
        await EnterAsync(harness);

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Facts(3, keys: 1));

        Bubbles(harness).Should().BeEmpty();
    }
}
