using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Grains.Zones.Messaging;
using Vortex.Rooms.Grains.Zones.Runtime;

namespace Vortex.Rooms.Grains.Zones.Modules;

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

        // One bubble per step: the highest named area just entered, with what it allows.
        if (!Moved(visitor, now) && entered.FirstOrDefault(a => a.Name.Length > 0) is { } named)
        {
            string rules = Describe(zone.RulesAt(x, y));

            ZoneBubble.Show(
                roomGrain,
                playerId,
                "zone.area",
                rules.Length > 0
                    ? $"Vous entrez : {named.Name}\n{rules}"
                    : $"Vous entrez : {named.Name}"
            );
        }
    }

    /// <summary>
    /// Whether the player may step onto (x, y): a Restricted area they are not already in lets in
    /// only a player who meets its conditions, and one with none lets in nobody until a trigger
    /// switches it off. Refused, the player hears why.
    /// </summary>
    public bool MayStepOnto(IRoomAvatar avatar, int x, int y)
    {
        // Every step of every player comes here: a cell outside any Restricted area answers from
        // its cached rules and goes no further.
        if (
            roomGrain._state.ZoneRuntime is not { } zone
            || !zone.RulesAt(x, y).Restricted
            || avatar is not IRoomPlayer player
            || player.PlayerId.Value <= 0
        )
        {
            return true;
        }

        IReadOnlyList<ZoneArea> current = zone.Visitors.TryGetValue(
            player.PlayerId,
            out ZoneVisitor? visitor
        )
            ? visitor.Areas
            : [];
        ZoneArea? closed = zone.ActiveAreasAt(x, y)
            .Where(a => a.Kind == ZoneAreaKinds.Restricted && !current.Contains(a))
            .FirstOrDefault(a =>
                a.Conditions.Count == 0
                || !roomGrain.ZoneTriggers.ConditionsHold(a.Conditions, player.PlayerId)
            );

        if (closed is null)
        {
            return true;
        }

        ZoneBubble.Show(
            roomGrain,
            player.PlayerId,
            "zone.restricted",
            closed.Name.Length > 0 ? $"Accès restreint : {closed.Name}" : "Accès restreint"
        );

        return false;
    }

    private static string Describe(ZoneCellRules rules) =>
        string.Join(
            ", ",
            new[]
            {
                rules.Safe ? "Zone sûre" : null,
                rules.Pvp ? "Zone PvP" : null,
                rules.Hostile ? "Zone hostile" : null,
                rules.NoMount ? "Sans monture" : null,
                rules.Restricted ? "Accès restreint" : null,
            }.OfType<string>()
        );

    /// <summary>
    /// A trigger set off by one of this step's events moved the player (a LeaveZone barrier that
    /// teleports them back): the newer update has said everything, and the rest of this one
    /// describes a step that no longer happened.
    /// </summary>
    private static bool Moved(ZoneVisitor visitor, IReadOnlyList<ZoneArea> now) =>
        !ReferenceEquals(visitor.Areas, now);
}
