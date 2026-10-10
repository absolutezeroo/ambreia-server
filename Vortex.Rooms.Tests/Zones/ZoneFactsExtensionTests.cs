using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Spec Z1 §11: a module the rooms have never heard of — a quest log — declares its part of the
/// zone's facts, its condition and what its changes set off, and the zone runs them: its QuestState
/// trigger fires and its condition gates an area, with nothing in the rooms naming it.
/// </summary>
public sealed class ZoneFactsExtensionTests
{
    private readonly Dictionary<PlayerId, TestQuestFacts> _logs = [];

    private static TestQuestFacts Log(long sequence, string state) =>
        new()
        {
            Sequence = sequence,
            States = ImmutableDictionary<string, string>.Empty.Add("q1", state),
        };

    private async Task<RoomHarness> ZoneAsync()
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Zones =
            [
                Area("camp", ZoneAreaKinds.Restricted, "Camp", 3, 5) with
                {
                    Conditions = [new QuestStateCondition("q1", "done")],
                },
            ],
            Triggers =
            [
                new ZoneTrigger
                {
                    Id = "t_q1",
                    Type = ZoneTriggerType.QuestState,
                    Target = "q1",
                    Actions = [new ShowDialogueAction("quête")],
                },
            ],
        };
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            doc,
            conditions: [new TestQuestStateEvaluator()],
            sources: [new TestQuestSource(_logs)]
        );
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);
        _logs[Walker] = Log(1, "started");
        await EnterAsync(harness);

        return harness;
    }

    [Fact]
    public async Task ItsChange_SetsOffItsTrigger()
    {
        RoomHarness harness = await ZoneAsync();

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Log(2, "done"));

        Sent<NotificationDialogMessageComposer>(harness)
            .Select(n => n.Parameters.Single(p => p.Key == "message").Value)
            .Should()
            .Equal("quête");
    }

    [Fact]
    public async Task ItsCondition_GatesAnArea()
    {
        RoomHarness harness = await ZoneAsync();

        await WalkAsync(harness, 4, 2);
        AvatarOf(harness).X.Should().BeLessThan(3, "the quest is not done");

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Log(2, "done"));
        await WalkAsync(harness, 4, 2);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().Be((4, 2));
    }
}
