using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Outgoing.Nft;

namespace Vortex.Players.Grains;

internal sealed partial class PlayerGrain
{
    public async Task<List<int>> GetOwnedChatStyleIdsAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        // The join is the point: the owned row keys the catalogue row, and it is the catalogue's
        // ClientStyleId -- not our primary key -- that the client matches against its own library.
        return await dbCtx
            .PlayerOwnedChatStyles.AsNoTracking()
            .Where(o => o.PlayerEntityId == _state.PlayerId.Value && o.DeletedAt == null)
            .Select(o => o.ChatStyle.ClientStyleId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct)
            .ConfigureAwait(true);
    }

    public async Task<bool> GrantChatStyleAsync(int clientStyleId, CancellationToken ct)
    {
        if (!ChatStyleBands.IsGrantable(clientStyleId))
        {
            return false;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerChatStyleEntity? style = await dbCtx
            .PlayerChatStyles.FirstOrDefaultAsync(s => s.ClientStyleId == clientStyleId, ct)
            .ConfigureAwait(true);

        if (style is null)
        {
            // Created on first grant rather than seeded. The ids are the CLIENT's -- they come out
            // of its own chatstyles library and an operator cannot invent one -- so a table listing
            // them in advance would be a copy that goes stale the next time the client ships.
            //
            // Two players granted the same never-before-seen style at the same instant would race
            // on the unique index and one would throw; the operator retries and the row is there.
            style = new PlayerChatStyleEntity { ClientStyleId = clientStyleId };
            dbCtx.PlayerChatStyles.Add(style);
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }

        PlayerChatStyleOwnedEntity? owned = await dbCtx
            .PlayerOwnedChatStyles.FirstOrDefaultAsync(
                o => o.PlayerEntityId == _state.PlayerId.Value && o.ChatStyleId == style.Id,
                ct
            )
            .ConfigureAwait(true);

        if (owned is not null && owned.DeletedAt is null)
        {
            // Already held. Reported as success on purpose: a reward-track step and an operator
            // both retry, and neither should read "you already have it" as a failure.
            return true;
        }

        if (owned is not null)
        {
            owned.DeletedAt = null;
        }
        else
        {
            PlayerEntity? player = await dbCtx
                .Players.FirstOrDefaultAsync(p => p.Id == _state.PlayerId.Value, ct)
                .ConfigureAwait(true);

            if (player is null)
            {
                return false;
            }

            dbCtx.PlayerOwnedChatStyles.Add(
                new PlayerChatStyleOwnedEntity
                {
                    PlayerEntityId = _state.PlayerId.Value,
                    ChatStyleId = style.Id,
                    PlayerEntity = player,
                    ChatStyle = style,
                }
            );
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        await PushChatStyleChangeAsync(clientStyleId, added: true, ct).ConfigureAwait(true);

        return true;
    }

    public async Task<bool> RevokeChatStyleAsync(int clientStyleId, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerChatStyleOwnedEntity? owned = await dbCtx
            .PlayerOwnedChatStyles.FirstOrDefaultAsync(
                o =>
                    o.PlayerEntityId == _state.PlayerId.Value
                    && o.ChatStyle.ClientStyleId == clientStyleId
                    && o.DeletedAt == null,
                ct
            )
            .ConfigureAwait(true);

        if (owned is null)
        {
            return false;
        }

        dbCtx.PlayerOwnedChatStyles.Remove(owned);
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        await PushChatStyleChangeAsync(clientStyleId, added: false, ct).ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// Tells a connected client that one style moved, on whichever message its band is read from.
    /// </summary>
    /// <remarks>
    /// The two bands refresh differently and neither is a choice. A purchasable style has its own
    /// incremental message: <c>SessionDataManager</c> pushes or removes the single id and raises
    /// <c>SDTWE_PURCHASABLE_STYLES_UPDATED</c>. An unlockable one has nothing of the kind, and
    /// <c>onNftChatStyles</c> replaces its vector outright, so re-sending that whole band is the
    /// only refresh there is. Without either, a style granted mid-session stays invisible until the
    /// player reconnects.
    /// </remarks>
    private async Task PushChatStyleChangeAsync(int clientStyleId, bool added, CancellationToken ct)
    {
        IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain((long)_state.PlayerId);

        if (ChatStyleBands.IsPurchasable(clientStyleId))
        {
            await presence
                .SendComposerAsync(
                    new UserPurchasableChatStyleChangedMessageComposer
                    {
                        Added = added,
                        ChatStyleId = clientStyleId,
                    }
                )
                .ConfigureAwait(true);

            return;
        }

        List<int> styleIds = await GetOwnedChatStyleIdsAsync(ct).ConfigureAwait(true);

        await presence
            .SendComposerAsync(
                new UserNftChatStylesMessageComposer
                {
                    ChatStyleIds = styleIds.FindAll(ChatStyleBands.IsUnlockable),
                }
            )
            .ConfigureAwait(true);
    }
}
