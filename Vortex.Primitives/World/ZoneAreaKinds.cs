using System.Collections.Generic;

namespace Vortex.Primitives.World;

/// <summary>
/// The sorts of area a zone can draw (plan D6, D24, spec §20.1). Safe, NoCombat, PvP and Hostile
/// rule combat at a cell; NoMount and Restricted hold wherever they lie; the others name what the
/// place is (a quest, a boss, a gathering spot…) for the systems that come with the RPG.
/// </summary>
public static class ZoneAreaKinds
{
    public const string Safe = "Safe";
    public const string NoCombat = "NoCombat";
    public const string PvP = "PvP";
    public const string Hostile = "Hostile";
    public const string Town = "Town";
    public const string Quest = "Quest";
    public const string Boss = "Boss";
    public const string Dungeon = "Dungeon";
    public const string Gathering = "Gathering";
    public const string Audio = "Audio";
    public const string Event = "Event";
    public const string NoMount = "NoMount";

    /// <summary>Only a player who meets the area's conditions may step in; none = nobody.</summary>
    public const string Restricted = "Restricted";

    public const string Custom = "Custom";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Safe,
        NoCombat,
        PvP,
        Hostile,
        Town,
        Quest,
        Boss,
        Dungeon,
        Gathering,
        Audio,
        Event,
        NoMount,
        Restricted,
        Custom,
    };
}
