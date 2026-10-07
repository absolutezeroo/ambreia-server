using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>Walks the player onto the furni; a seat (its logic or the zone's collision) sits them.</summary>
public sealed class ZoneSitInteraction : IZoneInteraction
{
    public string Kind => "sit";

    public Task UseAsync(ZoneUse use, CancellationToken ct) =>
        use.Room.AvatarModule.WalkAvatarToAsync(use.Ctx, use.Item.X, use.Item.Y, null, ct);
}
