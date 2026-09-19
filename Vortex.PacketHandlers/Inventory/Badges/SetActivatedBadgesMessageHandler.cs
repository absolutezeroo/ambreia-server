using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Snapshots;
using Vortex.Protocol.Messages.Incoming.Inventory.Badges;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Inventory.Badges;

public class SetActivatedBadgesMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<SetActivatedBadgesMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        SetActivatedBadgesMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        ImmutableArray<PlayerBadgeSnapshot> equipped = await _grainFactory
            .GetPlayerBadgeGrain(ctx.PlayerId)
            .SetActivatedBadgesAsync(message.Slots, ct)
            .ConfigureAwait(false);

        HabboUserBadgesMessageComposer composer = new()
        {
            UserId = ctx.PlayerId,
            Badges = equipped,
        };

        // Equipping is only half the exchange: the official server answers SetActivatedBadges with
        // HabboUserBadges, and both of the client's listeners for that message are fed by the one
        // packet -- RoomUsersHandler.onUserBadges repaints the avatar in the room, the inventory's
        // BadgesModel.onUserBadges repaints the badge editor. Saving and staying silent left a new
        // selection invisible to everybody, the player who just made it included, until something
        // else happened to ask for it.
        if (ctx.RoomId > 0)
        {
            await _grainFactory
                .GetRoomCore(ctx.RoomId)
                .SendComposerToRoomAsync(composer)
                .ConfigureAwait(false);
        }
        else
        {
            await ctx.SendComposerAsync(composer, ct).ConfigureAwait(false);
        }
    }
}
