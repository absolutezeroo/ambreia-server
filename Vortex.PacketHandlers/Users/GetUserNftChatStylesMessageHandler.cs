using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Nft;

namespace Vortex.PacketHandlers.Users;

/// <summary>
/// Which chat-bubble styles this player has, in both bands.
/// </summary>
/// <remarks>
/// <para>
/// <c>RoomChatInputView</c> walks its own style library and, for every id it classes as an unlock
/// (1000..9999), keeps the style out of the selector unless
/// <c>SessionDataManager.hasNftChatStyle(id)</c> says so — and that list comes from nowhere but this
/// answer. So an empty reply is not a neutral one: it is the only reason a player would see none of
/// the twenty-five unlockable bubbles the client ships with.
/// </para>
/// <para>
/// It answers <em>two</em> messages, because the second has no request of its own.
/// <c>UserPurchasableChatStyles</c> is in the client's incoming registry with no composer opposite
/// it, so the server has to volunteer it — and this request is the right moment, because the client
/// sends it from <c>SessionDataManager.initSessionData</c>, at login, rather than when the
/// Collectors Guild opens. One query, split by band: an id on the wrong list is simply never
/// matched.
/// </para>
/// </remarks>
public class GetUserNftChatStylesMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<GetUserNftChatStylesMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        GetUserNftChatStylesMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        List<int> styleIds = await _grainFactory
            .GetPlayerGrain(ctx.PlayerId)
            .GetOwnedChatStyleIdsAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new UserNftChatStylesMessageComposer
                {
                    ChatStyleIds = styleIds.FindAll(ChatStyleBands.IsUnlockable),
                },
                ct
            )
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new UserPurchasableChatStylesMessageComposer
                {
                    ChatStyleIds = styleIds.FindAll(ChatStyleBands.IsPurchasable),
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
