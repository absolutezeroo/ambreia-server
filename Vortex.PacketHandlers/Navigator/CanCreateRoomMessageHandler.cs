using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Server.Grains;
using Vortex.Protocol.Messages.Incoming.Navigator;
using Vortex.Protocol.Messages.Outgoing.Navigator;

namespace Vortex.PacketHandlers.Navigator;

public class CanCreateRoomMessageHandler(
    INavigatorProvider navigatorProvider,
    IGrainFactory grainFactory
) : IMessageHandler<CanCreateRoomMessage>
{
    private readonly INavigatorProvider _navigatorProvider = navigatorProvider;
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        CanCreateRoomMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        // The limit is per club level, because the client's own answer to hitting it already is: a
        // VIP is told the plain limit, everyone else is shown ${navigator.createroom.vippromo},
        // "subscribe for more rooms". Read off the same IsVip the SSO handshake maps to the
        // client's ClubLevelType.Vip, so the two sides cannot disagree about who is a subscriber.
        ClubSubscriptionSnapshot sub = await _grainFactory
            .GetPlayerGrain(ctx.PlayerId)
            .GetClubSubscriptionAsync(ct)
            .ConfigureAwait(false);

        int maxRooms = await _grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(
                RoomsConfig.MaxRoomsKeyFor(sub.IsVip),
                RoomsConfig.MaxRoomsDefaultFor(sub.IsVip)
            )
            .ConfigureAwait(false);

        int ownedRooms = await _navigatorProvider
            .GetRoomCountByOwnerAsync(ctx.PlayerId, ct)
            .ConfigureAwait(false);

        int resultCode = ownedRooms >= maxRooms ? 1 : 0;

        await ctx.SendComposerAsync(
                new CanCreateRoomMessageComposer { ResultCode = resultCode, RoomLimit = maxRooms },
                ct
            )
            .ConfigureAwait(false);
    }
}
