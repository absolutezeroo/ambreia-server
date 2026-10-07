using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>Logged only: the client has no sound since the core cut (plan D17).</summary>
public sealed class PlaySoundExecutor : ZoneActionExecutor<PlaySoundAction>
{
    protected override Task RunAsync(
        PlaySoundAction action,
        ZoneTriggerContext ctx,
        CancellationToken ct
    )
    {
        ctx.Room._logger.LogDebug(
            "Zone sound {Sound} requested in room {RoomId}",
            action.Sound,
            ctx.Room.RoomId.Value
        );

        return Task.CompletedTask;
    }
}
