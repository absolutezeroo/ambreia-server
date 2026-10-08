using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Grains.Zones.Runtime;

namespace Vortex.Rooms.Grains.Zones.Compilation;

/// <summary>
/// The rules of a cell (plan D6, D24), the one place they are worked out: the bubble shows them,
/// the walk enforces Restricted, combat and monsters will read them. Switched-off areas count for
/// nothing.
/// </summary>
public static class ZoneRuleBook
{
    public static ZoneCellRules At(ZoneRuntimeState zone, int x, int y)
    {
        ZoneSettings settings = zone.Compiled.Document.Settings;
        IReadOnlyList<ZoneArea> areas = zone.ActiveAreasAt(x, y);

        // Highest priority first: the first area that says anything about combat decides it.
        string? combat = areas
            .Select(a => a.Kind)
            .FirstOrDefault(k =>
                k
                    is ZoneAreaKinds.Safe
                        or ZoneAreaKinds.NoCombat
                        or ZoneAreaKinds.PvP
                        or ZoneAreaKinds.Hostile
            );
        bool safe = combat is ZoneAreaKinds.Safe or ZoneAreaKinds.NoCombat;

        return new ZoneCellRules
        {
            Safe = safe,
            Pvp = combat == ZoneAreaKinds.PvP || !safe && settings.PvpAllowed,
            Hostile = combat == ZoneAreaKinds.Hostile || !safe && settings.Type == ZoneType.Dungeon,
            NoMount = areas.Any(a => a.Kind == ZoneAreaKinds.NoMount),
            Restricted = areas.Any(a => a.Kind == ZoneAreaKinds.Restricted),
            RecommendedLevel =
                areas.FirstOrDefault(a => a.RecommendedLevel > 0)?.RecommendedLevel
                ?? settings.RecommendedLevel,
            Music = areas.FirstOrDefault(a => a.Music.Length > 0)?.Music ?? settings.Music,
            Type = settings.Type,
        };
    }
}
