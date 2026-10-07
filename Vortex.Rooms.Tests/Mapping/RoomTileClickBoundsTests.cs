using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Mapping;

/// <summary>
/// What a tile click does with coordinates that are not a tile.
///
/// The click carries two raw ints off the wire on the most-sent packet of the game, and the room
/// used to flatten them into an array index without asking whether they named a tile at all. Out of
/// range that threw; in the fold — x equal to the room width lands on the first tile of the next row
/// — it published a click the player never made, from wherever they happened to be standing. Any
/// listener on that event would have fired on a tile nobody clicked.
/// </summary>
public sealed class RoomTileClickBoundsTests
{
    private const int ListenerX = 0;
    private const int ListenerY = 4;

    /// <summary>A room whose tile (0,4) is the only one anybody is listening to.</summary>
    private static async Task<RoomHarness> RoomListeningOnOneTileAsync()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        int listenerIdx = harness.Grain.MapModule.ToIdx(ListenerX, ListenerY);

        harness.Grain._state.TileFlags[listenerIdx] |= RoomTileFlags.TileClickListener;

        return harness;
    }

    private static async Task<int> ClicksSeenAsync(RoomHarness harness, int x, int y)
    {
        await harness
            .Grain.ClickTileAsync(
                harness.ContextFor(RoomHarness.Owner),
                x,
                y,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        return harness.RoomEvents.OfType<PlayerClickedTileEvent>().Count();
    }

    /// <summary>The control: the guard must not have simply switched tile clicks off.</summary>
    [Fact]
    public async Task ClickingTheListeningTile_PublishesTheClick()
    {
        RoomHarness harness = await RoomListeningOnOneTileAsync().ConfigureAwait(true);

        int seen = await ClicksSeenAsync(harness, ListenerX, ListenerY).ConfigureAwait(true);

        seen.Should().Be(1);
    }

    /// <summary>
    /// x = Width flattens to y * Width + Width, which is the first tile of the next row: clicking
    /// (12,3) in a 12-wide room used to fire the listener sitting on (0,4).
    /// </summary>
    [Fact]
    public async Task ClickingOneColumnPastTheEdge_DoesNotFireTheTileItFoldsOnto()
    {
        RoomHarness harness = await RoomListeningOnOneTileAsync().ConfigureAwait(true);

        int width = harness.Grain.MapModule.Width;

        int seen = await ClicksSeenAsync(harness, width, ListenerY - 1).ConfigureAwait(true);

        seen.Should().Be(0);
    }

    /// <summary>Off the map entirely, in both directions, is a no-op and not an exception.</summary>
    [Theory]
    [InlineData(-1, 3)]
    [InlineData(3, -1)]
    [InlineData(int.MaxValue, 3)]
    [InlineData(3, int.MaxValue)]
    public async Task ClickingOffTheMap_IsIgnored(int x, int y)
    {
        RoomHarness harness = await RoomListeningOnOneTileAsync().ConfigureAwait(true);

        int seen = await ClicksSeenAsync(harness, x, y).ConfigureAwait(true);

        seen.Should().Be(0);
    }
}
