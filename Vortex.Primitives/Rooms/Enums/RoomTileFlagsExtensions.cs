using System.Linq;

namespace Vortex.Primitives.Rooms.Enums;

public static class RoomTileFlagsExtensions
{
    public static RoomTileFlags Add(this RoomTileFlags current, RoomTileFlags toAdd) =>
        current | toAdd;

    public static RoomTileFlags Remove(this RoomTileFlags current, RoomTileFlags toRemove) =>
        current & ~toRemove;

    public static bool Has(this RoomTileFlags current, RoomTileFlags toCheck) =>
        (current & toCheck) != 0;

    public static bool Has(this RoomTileFlags current, params RoomTileFlags[] toCheck) =>
        toCheck.Any(flag => (current & flag) != 0);
}
