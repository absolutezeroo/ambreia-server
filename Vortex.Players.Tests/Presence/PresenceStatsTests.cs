using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Players.Tests.Support;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Presence;

/// <summary>Spec M1a §4.4: the character's new stats go to the zone room the player stands in,
/// through his presence, without anybody waiting on the room.</summary>
public sealed class PresenceStatsTests
{
    private const int PLAYER_ID = 42;

    private readonly List<string> _roomCalls = [];

    [Fact]
    public async Task ThePresence_ForwardsStatsToItsRoom()
    {
        PlayerPresenceGrain grain = Build();
        grain._state.ActiveRoomId = new RoomId(9);

        await grain.OnCharacterStatsChangedAsync(
            new CharacterStatsSnapshot { MaxHealth = 120 },
            CancellationToken.None
        );

        _roomCalls.Should().Equal("room 9: player 42 max 120");
    }

    [Fact]
    public async Task WithoutARoom_NothingIsForwarded()
    {
        PlayerPresenceGrain grain = Build();

        await grain.OnCharacterStatsChangedAsync(
            new CharacterStatsSnapshot { MaxHealth = 120 },
            CancellationToken.None
        );

        _roomCalls.Should().BeEmpty();
    }

    private PlayerPresenceGrain Build() =>
        GrainActivationContext.CreateWithIntegerKey<PlayerPresenceGrain>(
            PLAYER_ID,
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name == nameof(IGrainFactory.GetGrain)
                    ? FakeProxy.Create<IRoomCombat>(inner =>
                    {
                        _roomCalls.Add(
                            $"room {(long)call.Args![0]!}: player {((PlayerId)inner.Args![0]!).Value} max {((CharacterStatsSnapshot)inner.Args[1]!).MaxHealth}"
                        );

                        return Task.CompletedTask;
                    })
                    : null
            ),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            NullLogger<PlayerPresenceGrain>.Instance,
            FakeProxy.Create<IVortexMetrics>(_ => null),
            Options.Create(new PlayerPresenceConfig()),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IZoneRoomSource>(_ => null),
            TestChat.Pipeline()
        );
}
