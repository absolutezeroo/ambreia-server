using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Incoming.Room.Pets;

namespace Vortex.PacketHandlers.Room.Pets;

public class CustomizePetWithFurniMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<CustomizePetWithFurniMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        CustomizePetWithFurniMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0 || message.PetId <= 0 || message.FurniItemId <= 0)
        {
            return;
        }

        // Only the saddle is left on this packet: the room checks the furniture's category, the
        // same field the client uses to offer it.
        await _grainFactory
            .GetRoomPets(ctx.RoomId)
            .ApplySaddleToPetAsync(
                ctx.AsActionContext(),
                message.PetId,
                new RoomObjectId(message.FurniItemId),
                ct
            )
            .ConfigureAwait(false);
    }
}
