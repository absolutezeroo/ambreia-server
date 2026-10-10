using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Rpg.Characters.Zones;
using Vortex.Rpg.Zones.Rules;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Spec Z1 §7: a player under the recommended level of the zone or of an area they enter is told
/// so, once per area and visit. The room never reads the level: it asks the document's own
/// condition, and says nothing when nobody can answer it.
/// </summary>
public sealed class ZoneLevelAdviceTests
{
    private static long _sequence;

    private static CharacterZoneFacts Level(int level) =>
        new()
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Level = level,
            Items = ImmutableDictionary<string, int>.Empty,
        };

    private static ZoneDocument Deep(int recommended) =>
        ZoneSamples.Minimal() with
        {
            Zones =
            [
                Area("deep", "Custom", string.Empty, 3, 5) with
                {
                    RecommendedLevel = recommended,
                },
            ],
        };

    private static async Task<RoomHarness> InsideAsync(
        ZoneDocument doc,
        int? level,
        bool levelAnswered = true
    )
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            doc,
            conditions: levelAnswered ? [new PlayerLevelEvaluator()] : []
        );
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);

        if (level is { } l)
        {
            harness.Facts[Walker] = Level(l);
        }

        await EnterAsync(harness);

        return harness;
    }

    private static string[] Advice(RoomHarness harness) =>
        [
            .. Sent<NotificationDialogMessageComposer>(harness)
                .Where(n => n.Type == "zone.level")
                .Select(n => n.Parameters.Single(p => p.Key == "message").Value),
        ];

    [Fact]
    public async Task UnderTheLevel_ThePlayerIsWarned()
    {
        RoomHarness harness = await InsideAsync(Deep(5), level: 2);

        await WalkAsync(harness, 4, 2);

        Advice(harness).Should().Equal("Niveau conseillé : 5.");
    }

    [Fact]
    public async Task AtTheLevel_Nothing()
    {
        RoomHarness harness = await InsideAsync(Deep(5), level: 5);

        await WalkAsync(harness, 4, 2);

        Advice(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task OncePerAreaAndVisit()
    {
        RoomHarness harness = await InsideAsync(Deep(5), level: 2);

        await WalkAsync(harness, 4, 2);
        await WalkAsync(harness, 1, 2);
        await WalkAsync(harness, 4, 2);
        Advice(harness).Should().HaveCount(1, "the same visit");

        await harness.Grain.RemoveAvatarFromPlayerAsync(
            harness.ContextFor(Walker),
            Walker,
            default
        );
        await EnterAsync(harness);
        await WalkAsync(harness, 4, 2);

        Advice(harness).Should().HaveCount(2, "a new visit");
    }

    [Fact]
    public async Task WithoutFacts_Nothing()
    {
        RoomHarness harness = await InsideAsync(Deep(5), level: null);

        await WalkAsync(harness, 4, 2);

        Advice(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task NobodyAnswersLevel_Nothing()
    {
        RoomHarness harness = await InsideAsync(Deep(5), level: 2, levelAnswered: false);

        await WalkAsync(harness, 4, 2);

        Advice(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task ALevelOneAdvice_SaysNothing()
    {
        RoomHarness harness = await InsideAsync(Deep(1), level: 1);

        await WalkAsync(harness, 4, 2);

        Advice(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task TheZonesOwnLevel_OnArrival()
    {
        ZoneDocument doc = ZoneSamples.Minimal();
        doc = doc with { Settings = doc.Settings with { RecommendedLevel = 8 } };

        RoomHarness harness = await InsideAsync(doc, level: 3);

        Advice(harness).Should().Equal("Niveau conseillé : 8.");
    }
}
