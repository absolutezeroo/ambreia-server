using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Grains;

namespace Vortex.Rooms.Tests.Support;

/// <summary>
/// Reads a room's live state and reports the ways its three representations of an item disagree.
/// </summary>
/// <remarks>
/// <para>
/// An item exists in a room three times over: in <c>ItemsById</c>, on the tiles of its footprint,
/// and in the logic index. Every mutation has to move all three, and when one is missed nothing
/// fails — the room keeps serving, and the disagreement shows up later as a tile nobody can walk
/// on, a logic lookup returning furniture that left, or an item that cannot be placed again.
/// </para>
/// <para>
/// This deliberately shares no arithmetic with the code it checks. It never calls
/// <c>GetTileIdForSize</c>, which is the helper that decides a footprint in the first place: an
/// assertion that recomputes the answer the same way agrees with the mutation whether or not the
/// mutation is right. What it uses instead is correspondence — which ids appear where — and a tile
/// count taken from the definition alone, which holds at any rotation because a rotated footprint
/// swaps its sides without changing its area.
/// </para>
/// </remarks>
internal static class RoomFurnitureInvariants
{
    /// <summary>Every disagreement found, in a form a failing test can print. Empty means the three
    /// representations agree.</summary>
    public static IReadOnlyList<string> Violations(RoomLiveState state)
    {
        List<string> violations = [];

        CheckTilesHoldOnlyLiveItems(state, violations);
        CheckFloorItemsAreOnTheirFootprint(state, violations);
        CheckIndexHoldsOnlyLiveItems(state, violations);

        return violations;
    }

    /// <summary>A tile holding an id the room no longer has is what a detach that forgot the map, or
    /// an attach that registered twice, leaves behind — and it is enough to make the tile answer for
    /// a height and an occupancy that belong to furniture somewhere else entirely.</summary>
    private static void CheckTilesHoldOnlyLiveItems(RoomLiveState state, List<string> violations)
    {
        for (int idx = 0; idx < state.TileFloorStacks.Length; idx++)
        {
            foreach (RoomObjectId objectId in state.TileFloorStacks[idx])
            {
                if (!state.ItemsById.ContainsKey(objectId))
                {
                    violations.Add(
                        $"tile {idx} holds item {objectId.Value}, which is not in ItemsById"
                    );
                }
            }
        }
    }

    /// <summary>The other direction, plus the size: a floor item stands on exactly as many tiles as
    /// its definition covers. One tile too many is a footprint that was registered from the wrong
    /// coordinates and never taken back; none at all is an attach that never reached the map.</summary>
    private static void CheckFloorItemsAreOnTheirFootprint(
        RoomLiveState state,
        List<string> violations
    )
    {
        foreach (IRoomItem item in state.ItemsById.Values)
        {
            if (item is not IRoomFloorItem floor)
            {
                continue;
            }

            int held = Enumerable
                .Range(0, state.TileFloorStacks.Length)
                .Count(idx => state.TileFloorStacks[idx].Contains(floor.ObjectId));

            int expected = floor.Definition.Width * floor.Definition.Length;

            if (held != expected)
            {
                violations.Add(
                    $"floor item {floor.ObjectId.Value} ({floor.Definition.Width}x{floor.Definition.Length}) "
                        + $"is on {held} tiles, expected {expected}"
                );
            }
        }
    }

    /// <summary>An index bucket that kept an item the room dropped keeps answering ItemsOf&lt;T&gt;()
    /// for it, so whoever asks acts on furniture that is no longer in the room.</summary>
    private static void CheckIndexHoldsOnlyLiveItems(RoomLiveState state, List<string> violations)
    {
        foreach (IRoomItem indexed in state.ItemIndex.IndexedItems())
        {
            if (!state.ItemsById.TryGetValue(indexed.ObjectId, out IRoomItem? live))
            {
                violations.Add(
                    $"the logic index holds item {indexed.ObjectId.Value}, which is not in ItemsById"
                );

                continue;
            }

            if (!ReferenceEquals(live, indexed))
            {
                violations.Add(
                    $"the logic index holds a stale instance of item {indexed.ObjectId.Value} "
                        + "-- a definition swap replaced the object without reindexing"
                );
            }
        }
    }
}
