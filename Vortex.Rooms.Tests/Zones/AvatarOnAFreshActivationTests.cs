using System;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// A room entry is several grain calls, and the activation can go between them -- a zone republished
/// while a player is still entering deactivates it. The avatar then lands in a fresh activation that
/// never built its map, and every step threw IndexOutOfRangeException (found by driver.mjs zone).
/// </summary>
public sealed class AvatarOnAFreshActivationTests
{
    [Fact]
    public async Task CreatingAnAvatar_BuildsTheMapWhenTheActivationHasNone()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);
        harness.Grain._state.IsMapReady = false;
        harness.Grain._state.TileAvatarStacks = [];

        bool created = await harness.Grain.CreateAvatarFromPlayerAsync(
            harness.ContextFor(new PlayerId(7)),
            new PlayerSummarySnapshot
            {
                PlayerId = new PlayerId(7),
                Name = "Builder",
                Motto = string.Empty,
                Figure = "hd-180-1",
                Gender = AvatarGenderType.Male,
                AchievementScore = 0,
                CreatedAt = DateTime.UtcNow,
            },
            default
        );

        created.Should().BeTrue();
        harness.Grain._state.TileAvatarStacks.Length.Should().Be(36);
        harness.Grain._state.AvatarsByPlayerId.Should().ContainKey(new PlayerId(7));
    }
}
