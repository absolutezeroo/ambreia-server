using Vortex.Primitives.Players;

namespace Vortex.Primitives.World;

/// <summary>Who shares a dungeon copy (plan D19).</summary>
public static class ZoneInstanceGroups
{
    // ponytail: no party system yet, so a group is the player alone; the party id replaces this key.
    public static string KeyFor(PlayerId playerId) => $"player:{playerId.Value}";
}
