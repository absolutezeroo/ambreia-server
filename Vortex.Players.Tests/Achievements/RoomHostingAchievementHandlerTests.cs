using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Progression.Achievements;
using Vortex.Progression.Achievements.Events;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Achievements;

public sealed class RoomHostingAchievementHandlerTests
{
    private const int OWNER = 7;

    [Theory]
    [InlineData(42, 150, 2)] // a visitor's whole minutes go to the owner
    [InlineData(42, 59, 0)] // under a minute is nothing
    [InlineData(OWNER, 600, 0)] // the owner in their own room hosts nobody
    public async Task Credits_the_owner_with_a_visitors_whole_minutes(
        int visitor,
        long seconds,
        int expectedMinutes
    )
    {
        List<(long PlayerId, string Name, int Amount)> credited = [];

        await new RoomHostingAchievementHandler(Factory(credited))
            .HandleAsync(
                new PlayerLeftRoomEvent((PlayerId)visitor, 1, DateTime.UtcNow, seconds),
                new EventContext { CorrelationId = "t" },
                CancellationToken.None
            )
            .ConfigureAwait(true);

        if (expectedMinutes == 0)
        {
            credited.Should().BeEmpty();
            return;
        }

        credited.Should().Equal((OWNER, AchievementNames.RoomDecoHosting, expectedMinutes));
    }

    private static IGrainFactory Factory(List<(long, string, int)> credited) =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            if (!call.Method.IsGenericMethod)
            {
                return null;
            }

            Type type = call.Method.GetGenericArguments()[0];
            long key = call.Args?[0] is long k ? k : 0;

            if (type == typeof(IRoomCore))
            {
                return FakeProxy.Create<IRoomCore>(c =>
                    c.Method.Name == nameof(IRoomCore.GetSummaryAsync)
                        ? Task.FromResult(
                            new RoomSummarySnapshot
                            {
                                RoomId = 1,
                                Name = "r",
                                Description = "",
                                OwnerId = OWNER,
                                OwnerName = "o",
                                Population = 0,
                                LastUpdatedUtc = DateTime.UtcNow,
                            }
                        )
                        : null
                );
            }

            if (type == typeof(IPlayerAchievementGrain))
            {
                return FakeProxy.Create<IPlayerAchievementGrain>(c =>
                {
                    if (c.Method.Name == nameof(IPlayerAchievementGrain.ProgressAsync))
                    {
                        credited.Add((key, (string)c.Args![0]!, (int)c.Args[1]!));
                    }

                    return null;
                });
            }

            return FakeProxy.CreateFor(type, _ => null);
        });
}
