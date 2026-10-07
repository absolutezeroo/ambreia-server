namespace Vortex.Primitives.World;

/// <summary>
/// The sorts of entry the server knows (plan D4). An unknown sort stays valid: it is simply never
/// chosen by default.
/// </summary>
public static class ZoneEntryKinds
{
    /// <summary>Where a player arrives when nothing names an entry.</summary>
    public const string Arrival = "arrival";

    public const string Respawn = "respawn";

    public const string Teleport = "teleport";

    public const string DungeonEntry = "dungeon_entry";

    public const string DungeonReturn = "dungeon_return";
}
