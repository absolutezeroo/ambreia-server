using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Areas at run time (plan D5–D7): stepping in or out is a room event, and a named area greets the
/// player with its name and its rules.
/// </summary>
public sealed class ZoneAreaTests
{
    private static ZoneDocument Market() =>
        ZoneSamples.Minimal() with
        {
            Zones = [Area("market", "Safe", "Marché", 3, 5)],
        };

    [Fact]
    public async Task WalkingIntoANamedArea_IsAnEventAndABubble()
    {
        RoomHarness harness = await ZoneAsync(Market());
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        harness
            .RoomEvents.OfType<ZoneAreaEnteredEvent>()
            .Select(e => e.AreaId)
            .Should()
            .Equal("market");
        Sent<NotificationDialogMessageComposer>(harness)
            .Should()
            .ContainSingle()
            .Which.Parameters.Single(p => p.Key == "message")
            .Value.Should()
            .Contain("Marché")
            .And.Contain("Zone sûre");
    }

    [Fact]
    public async Task WalkingOut_IsTheLeaveEvent()
    {
        RoomHarness harness = await ZoneAsync(Market());
        await EnterAsync(harness);
        await WalkAsync(harness, 4, 2);

        await WalkAsync(harness, 1, 2);

        harness
            .RoomEvents.OfType<ZoneAreaLeftEvent>()
            .Select(e => e.AreaId)
            .Should()
            .Equal("market");
    }

    [Fact]
    public async Task ArrivingInsideAnArea_EntersIt()
    {
        RoomHarness harness = await ZoneAsync(
            Market() with
            {
                Entries = [Entry("gate", ZoneEntryKinds.Arrival, 4, 4)],
            }
        );

        await EnterAsync(harness);

        harness
            .RoomEvents.OfType<ZoneAreaEnteredEvent>()
            .Select(e => e.AreaId)
            .Should()
            .Equal("market");
    }

    [Fact]
    public async Task WalkingWithinAnArea_IsNotANewEntry()
    {
        RoomHarness harness = await ZoneAsync(Market());
        await EnterAsync(harness);
        await WalkAsync(harness, 3, 2);

        await WalkAsync(harness, 5, 4);

        harness.RoomEvents.OfType<ZoneAreaEnteredEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task AHostileArea_SaysSoOnEntry()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Zones = [Area("woods", ZoneAreaKinds.Hostile, "Bois sombre", 3, 5)],
            }
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        BubbleText(harness).Should().Contain("Bois sombre").And.Contain("Zone hostile");
    }

    [Fact]
    public async Task ARestrictedArea_StopsAPlayerWhoDoesNotQualify()
    {
        RoomHarness harness = await ZoneAsync(Vault(new PlayerLevelCondition(5, 99)));
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        AvatarOf(harness).X.Should().BeLessThan(3, "the vault begins on column 3");
        BubbleText(harness).Should().Contain("Accès restreint");
    }

    [Fact]
    public async Task ARestrictedArea_LetsInAPlayerWhoQualifies()
    {
        RoomHarness harness = await ZoneAsync(Vault(new TimeOfDayCondition(0, 0)));
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().Be((4, 2));
    }

    [Fact]
    public async Task ASwitchedOffRestrictedArea_LetsEveryoneIn()
    {
        RoomHarness harness = await ZoneAsync(Vault());
        harness.Grain._state.ZoneRuntime!.SetAreaActive("vault", false);
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().Be((4, 2));
    }

    [Fact]
    public async Task TheRoom_AnswersWhatACellAllows()
    {
        // What combat, monsters and mounts will ask before they act.
        RoomHarness harness = await ZoneAsync(Market());

        ZoneCellRules? market = await harness.Grain.GetRulesAtAsync(4, 2);
        ZoneCellRules? road = await harness.Grain.GetRulesAtAsync(1, 2);

        market!.Safe.Should().BeTrue();
        road!.Safe.Should().BeFalse();
    }

    /// <summary>Columns 3 to 5, open only to whoever meets <paramref name="conditions"/>; none = to nobody.</summary>
    private static ZoneDocument Vault(params ZoneCondition[] conditions) =>
        ZoneSamples.Minimal() with
        {
            Zones =
            [
                Area("vault", ZoneAreaKinds.Restricted, "Chambre forte", 3, 5) with
                {
                    Conditions = [.. conditions],
                },
            ],
        };

    private static string BubbleText(RoomHarness harness) =>
        string.Join(
            "\n",
            Sent<NotificationDialogMessageComposer>(harness)
                .SelectMany(n => n.Parameters)
                .Select(p => p.Value)
        );
}
