using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Outgoing.Nft;

namespace Vortex.Players.Grains;

internal sealed partial class PlayerGrain
{
    /// <summary>
    /// The first style id an unlock can reach. Below this the client decides for itself — a static
    /// style is free, HC-only, ambassador-only or staff-overridable, and <c>RoomChatInputView</c>
    /// never asks the server about one. Granting id 12 would write a row nothing would ever read.
    /// </summary>
    private const int FirstUnlockableChatStyleId = 1000;

    /// <summary>
    /// One past the last id <c>UserNftChatStyles</c> governs. From 10000 up the client asks
    /// <c>hasPurchasableChatStyle</c> instead, which reads a different message
    /// (<c>UserPurchasableChatStyles</c>, header 3774) that nothing composes yet. Accepting a grant
    /// up there would store an unlock the client can never be told about.
    /// </summary>
    private const int FirstPurchasableChatStyleId = 10000;

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
        if (
            clientStyleId < FirstUnlockableChatStyleId
            || clientStyleId >= FirstPurchasableChatStyleId
        )
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
        await PushChatStylesAsync(ct).ConfigureAwait(true);

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
        await PushChatStylesAsync(ct).ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// Re-sends the whole list unasked.
    /// </summary>
    /// <remarks>
    /// The client only ever asks once, on the collectibles hub opening, and
    /// <c>SessionDataManager.onNftChatStyles</c> replaces its vector outright with whatever arrives
    /// — so a full re-send is the refresh, and there is no add/remove message to look for. Without
    /// it a style granted mid-session stays invisible until the player reconnects.
    /// </remarks>
    private async Task PushChatStylesAsync(CancellationToken ct)
    {
        List<int> styleIds = await GetOwnedChatStyleIdsAsync(ct).ConfigureAwait(true);

        await _grainFactory
            .GetPlayerPresenceGrain((long)_state.PlayerId)
            .SendComposerAsync(new UserNftChatStylesMessageComposer { ChatStyleIds = styleIds })
            .ConfigureAwait(true);
    }
}
