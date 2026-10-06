using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players.Grains;
using Vortex.Progression.Achievements;
using Vortex.Progression.Achievements.Events;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Achievements;

public sealed class ItemPlacedAchievementHandlerTests
{
    [Fact]
    public async Task A_hole_raises_both_bars_to_what_the_room_holds()
    {
        List<(string, int)> raised = await PlaceAsync(
            "furniture_floor_hole",
            kinds: 12,
            sameLogic: 3
        );

        raised
            .Should()
            .Equal(
                (AchievementNames.RoomDecoFurniTypeCount, 12),
                (AchievementNames.RoomDecoHoleFurniCount, 3)
            );
    }

    [Fact]
    public async Task Any_other_furni_raises_only_the_variety_bar()
    {
        List<(string, int)> raised = await PlaceAsync(
            "furniture_multistate",
            kinds: 4,
            sameLogic: 9
        );

        raised.Should().Equal((AchievementNames.RoomDecoFurniTypeCount, 4));
    }

    private static async Task<List<(string, int)>> PlaceAsync(
        string logic,
        int kinds,
        int sameLogic
    )
    {
        List<(string, int)> raised = [];

        IPlayerAchievementGrain grain = FakeProxy.Create<IPlayerAchievementGrain>(c =>
        {
            if (c.Method.Name == nameof(IPlayerAchievementGrain.ProgressToAsync))
            {
                raised.Add(((string)c.Args![0]!, (int)c.Args[1]!));
            }

            return null;
        });

        IGrainFactory factory = FakeProxy.Create<IGrainFactory>(call =>
            call.Method.IsGenericMethod ? grain : null
        );

        await new ItemPlacedAchievementHandler(factory)
            .HandleAsync(
                new ItemPlacedEvent(
                    1,
                    5,
                    5,
                    1,
                    null,
                    LogicName: logic,
                    KindsInRoom: kinds,
                    SameLogicInRoom: sameLogic
                ),
                new EventContext { CorrelationId = "t" },
                CancellationToken.None
            )
            .ConfigureAwait(true);

        return raised;
    }
}
