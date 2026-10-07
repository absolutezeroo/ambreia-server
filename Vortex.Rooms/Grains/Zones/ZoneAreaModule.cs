using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>
/// Which areas each player stands in (plan D5): a change is a room event, and entering a named
/// area shows its name and rules (D7).
/// </summary>
public sealed class ZoneAreaModule(RoomGrain roomGrain)
{
    public async Task UpdateAsync(PlayerId playerId, int x, int y, CancellationToken ct)
    {
        ZoneRuntimeState? zone = roomGrain._state.ZoneRuntime;

        if (zone is null)
        {
            return;
        }

        if (!zone.Visitors.TryGetValue(playerId, out ZoneVisitor? visitor))
        {
            zone.Visitors[playerId] = visitor = new ZoneVisitor();
        }

        IReadOnlyList<ZoneArea> before = visitor.Areas;
        IReadOnlyList<ZoneArea> now = zone.ActiveAreasAt(x, y);

        if (before.SequenceEqual(now))
        {
            return;
        }

        visitor.Areas = now;

        foreach (ZoneArea left in before.Except(now))
        {
            if (Moved(visitor, now))
            {
                return;
            }

            await roomGrain.EventModule.PublishAsync(
                new ZoneAreaLeftEvent
                {
                    RoomId = roomGrain.RoomId,
                    PlayerId = playerId,
                    AreaId = left.Id,
                },
                ct
            );
        }

        ZoneArea[] entered = [.. now.Except(before)];

        foreach (ZoneArea area in entered)
        {
            if (Moved(visitor, now))
            {
                return;
            }

            await roomGrain.EventModule.PublishAsync(
                new ZoneAreaEnteredEvent
                {
                    RoomId = roomGrain.RoomId,
                    PlayerId = playerId,
                    AreaId = area.Id,
                },
                ct
            );
        }

        // One bubble per step: the highest named area just entered.
        if (!Moved(visitor, now) && entered.FirstOrDefault(a => a.Name.Length > 0) is { } named)
        {
            string rule =
                zone.Compiled.SafeAt(x, y) ? "Zone sûre"
                : zone.Compiled.PvpAt(x, y) ? "Zone PvP"
                : string.Empty;

            ZoneBubble.Show(
                roomGrain,
                playerId,
                "zone.area",
                rule.Length > 0
                    ? $"Vous entrez : {named.Name}\n{rule}"
                    : $"Vous entrez : {named.Name}"
            );
        }
    }

    /// <summary>
    /// A trigger set off by one of this step's events moved the player (a LeaveZone barrier that
    /// teleports them back): the newer update has said everything, and the rest of this one
    /// describes a step that no longer happened.
    /// </summary>
    private static bool Moved(ZoneVisitor visitor, IReadOnlyList<ZoneArea> now) =>
        !ReferenceEquals(visitor.Areas, now);
}
