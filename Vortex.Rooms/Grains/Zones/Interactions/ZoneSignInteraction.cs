using System.Threading;
using System.Threading.Tasks;
using Vortex.Rooms.Grains.Zones.Messaging;
using Vortex.Rooms.Grains.Zones.Runtime;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>Shows the furni's tag to the player who read it.</summary>
public sealed class ZoneSignInteraction : IZoneInteraction
{
    public string Kind => "sign";

    public Task UseAsync(ZoneUse use, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(use.Decor.Tag))
        {
            ZoneBubble.Show(use.Room, use.Ctx.PlayerId, "zone.sign", use.Decor.Tag);
        }

        return Task.CompletedTask;
    }
}
