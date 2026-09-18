using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Nft;

namespace Vortex.PacketHandlers.Users;

/// <summary>
/// Which chat-bubble styles this player has unlocked.
/// </summary>
/// <remarks>
/// <para>
/// <c>RoomChatInputView</c> walks its own style library and, for every id it classes as an unlock
/// (1000..9999), keeps the style out of the selector unless
/// <c>SessionDataManager.hasNftChatStyle(id)</c> says so — and that list comes from nowhere but this
/// answer. So an empty reply is not a neutral one: it is the only reason a player sees none of the
/// twenty-five unlockable bubbles the client ships with.
/// </para>
/// <para>
/// Answering with every id the player owns, unfiltered. The client indexes the list by id and never
/// walks it, so an id outside the range it asks about costs nothing — and filtering here would be a
/// second copy of a range rule that already lives where grants are made.
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
                new UserNftChatStylesMessageComposer { ChatStyleIds = styleIds },
                ct
            )
            .ConfigureAwait(false);
    }
}
