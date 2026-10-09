using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Emote;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Avatars;

/// <summary>
/// RP actions (wave, laugh, dance…): the room shows a known action of a player standing here to
/// everyone in it, at most one per interval; anything else shows nothing.
/// </summary>
public sealed class RoomEmoteTests
{
    private RoomHarness _harness = null!;
    private RoomPlayerAvatar _owner = null!;

    private async Task OnePlayerAsync()
    {
        _harness = await RoomHarness.CreateAsync();
        _owner = _harness.PutRealPlayerInRoom(RoomHarness.Owner, 1, 1);
    }

    private Task EmoteAsync(PlayerId by, string key) =>
        _harness.Grain.EmoteAsync(_harness.ContextFor(by), key, CancellationToken.None);

    private RpgEmoteMessageComposer[] Shown() =>
        [.. _harness.BroadcastToRoom.OfType<RpgEmoteMessageComposer>()];

    [Fact]
    public async Task AKnownAction_IsShownToTheRoom()
    {
        await OnePlayerAsync();

        await EmoteAsync(RoomHarness.Owner, "wave");

        Shown()
            .Should()
            .Equal(new RpgEmoteMessageComposer { ObjectId = _owner.ObjectId.Value, Key = "wave" });
    }

    [Theory]
    [InlineData("fly")]
    [InlineData("")]
    [InlineData("WAVE")]
    public async Task AnUnknownAction_ShowsNothing(string key)
    {
        await OnePlayerAsync();

        await EmoteAsync(RoomHarness.Owner, key);

        Shown().Should().BeEmpty();
    }

    [Fact]
    public async Task APlayerWithoutAnAvatarHere_ShowsNothing()
    {
        await OnePlayerAsync();

        await EmoteAsync(RoomHarness.Stranger, "wave");

        Shown().Should().BeEmpty();
    }

    [Fact]
    public async Task ActionsFasterThanTheInterval_AreDropped()
    {
        await OnePlayerAsync();

        await EmoteAsync(RoomHarness.Owner, "wave");
        await EmoteAsync(RoomHarness.Owner, "dance");

        Shown().Select(e => e.Key).Should().Equal("wave");
    }
}
