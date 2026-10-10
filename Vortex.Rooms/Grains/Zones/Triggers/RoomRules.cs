using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Rooms.Grains.Zones.Triggers.Actions;
using Vortex.Rooms.Grains.Zones.Triggers.Conditions;

namespace Vortex.Rooms.Grains.Zones.Triggers;

/// <summary>
/// The rules the room answers itself, because they read or change the room: time of day, an
/// area's switch, dialogue, teleport, doors, sound. The RPG ones are declared by their modules.
/// </summary>
public static class RoomRules
{
    public static IReadOnlyList<IRoomConditionEvaluator> Conditions { get; } =
    [new TimeOfDayEvaluator(), new ZoneActiveEvaluator()];

    public static IReadOnlyList<IRoomActionExecutor> Actions { get; } =
    [
        new ShowDialogueExecutor(),
        new ActivateZoneExecutor(),
        new DeactivateZoneExecutor(),
        new TeleportExecutor(),
        new OpenDoorExecutor(),
        new PlaySoundExecutor(),
    ];

    /// <summary>Their types, declared to the editor's validator.</summary>
    public static IEnumerable<Type> Shipped =>
        Conditions.Select(c => c.ConditionType).Concat(Actions.Select(a => a.ActionType));
}
