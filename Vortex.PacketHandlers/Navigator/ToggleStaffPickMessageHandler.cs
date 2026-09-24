using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Navigator;

namespace Vortex.PacketHandlers.Navigator;

public class ToggleStaffPickMessageHandler(
    IGrainFactory grainFactory,
    IPermissionService permissionService
) : IMessageHandler<ToggleStaffPickMessage>
{
    public async ValueTask HandleAsync(
        ToggleStaffPickMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.RoomId <= 0)
        {
            return;
        }

        PermissionSet permissions = await permissionService
            .ResolveForPlayerAsync(ctx.PlayerId, ct)
            .ConfigureAwait(false);

        if (!permissions.Has(Capabilities.Navigator.StaffPick))
        {
            return;
        }

        IRoomSettings roomGrain = grainFactory.GetRoomSettings(message.RoomId);

        // message.IsStaffPicked is deliberately ignored. It is not the state the player asked for:
        // the client's button flips only its own caption and posts back the value it already had,
        // so honouring it wrote the value that was already stored and the pick never happened. The
        // grain flips the row it owns instead -- which is also what the reference emulator does,
        // reading the room id and nothing else.
        await roomGrain.ToggleStaffPickAsync(ctx.PlayerId, ct).ConfigureAwait(false);
    }
}
