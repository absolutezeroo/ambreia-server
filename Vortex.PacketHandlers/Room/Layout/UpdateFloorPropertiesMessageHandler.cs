using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Room.Layout;

namespace Vortex.PacketHandlers.Room.Layout;

/// <summary>
/// Save, from the Builders Club floor-plan editor.
///
/// The room the plan applies to is the one the sender is standing in — the composer carries no room
/// id, because the editor can only be opened from inside.
/// </summary>
public class UpdateFloorPropertiesMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<UpdateFloorPropertiesMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        UpdateFloorPropertiesMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        IRoomSettings room = _grainFactory.GetRoomSettings(ctx.RoomId);

        await room.UpdateFloorPlanAsync(
                ctx.PlayerId,
                new FloorPlanUpdate
                {
                    Model = message.Model,
                    DoorX = message.DoorX,
                    DoorY = message.DoorY,
                    DoorRotation = message.DoorRotation,
                    WallThickness = message.WallThickness,
                    FloorThickness = message.FloorThickness,
                    WallHeight = message.WallHeight,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
