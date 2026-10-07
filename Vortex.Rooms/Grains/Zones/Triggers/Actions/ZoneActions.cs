using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>
/// Runs an action by its kind. StartQuest and SpawnGroup belong to the RPG systems, not built yet:
/// they are logged and skipped.
/// </summary>
public static class ZoneActions
{
    private static readonly Dictionary<Type, IZoneActionExecutor> Executors =
        new IZoneActionExecutor[]
        {
            new ShowDialogueExecutor(),
            new ActivateZoneExecutor(),
            new DeactivateZoneExecutor(),
            new TeleportExecutor(),
            new OpenDoorExecutor(),
            new PlaySoundExecutor(),
        }.ToDictionary(e => e.ActionType);

    public static Task RunAsync(ZoneAction action, ZoneTriggerContext ctx, CancellationToken ct)
    {
        if (Executors.TryGetValue(action.GetType(), out IZoneActionExecutor? executor))
        {
            return executor.RunAsync(action, ctx, ct);
        }

        ctx.Room._logger.LogDebug(
            "Zone action {Action} has no effect before the RPG systems exist",
            action.GetType().Name
        );

        return Task.CompletedTask;
    }
}
