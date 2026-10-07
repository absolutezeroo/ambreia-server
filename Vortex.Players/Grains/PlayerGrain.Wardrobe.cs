using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans.Snapshots.Players;

namespace Vortex.Players.Grains;

internal sealed partial class PlayerGrain
{
    // The avatar editor exposes a fixed grid of slots (1..10, Membership/VIP-gated client-side). Reject
    // anything outside a small guard band so a malformed or hostile client can't grow the table
    // without bound.
    private const int MaxWardrobeSlotId = 30;

    public async Task<List<PlayerWardrobeOutfitSnapshot>> GetWardrobeAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        return await dbCtx
            .PlayerWardrobeOutfits.AsNoTracking()
            .Where(o => o.PlayerEntityId == _state.PlayerId.Value)
            .OrderBy(o => o.SlotId)
            .Select(o => new PlayerWardrobeOutfitSnapshot
            {
                SlotId = o.SlotId,
                Figure = o.Figure,
                Gender = o.Gender,
            })
            .ToListAsync(ct);
    }

    public async Task SaveWardrobeOutfitAsync(
        int slotId,
        string figure,
        string gender,
        CancellationToken ct
    )
    {
        // The columns are varchar(255)/varchar(8); a client that sends more would throw on the
        // insert rather than be rejected here.
        if (
            slotId < 1
            || slotId > MaxWardrobeSlotId
            || string.IsNullOrWhiteSpace(figure)
            || figure.Length > 255
            || gender.Length > 8
        )
        {
            return;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerWardrobeOutfitEntity? entity = await dbCtx.PlayerWardrobeOutfits.FirstOrDefaultAsync(
            o => o.PlayerEntityId == _state.PlayerId.Value && o.SlotId == slotId,
            ct
        );

        if (entity is null)
        {
            dbCtx.PlayerWardrobeOutfits.Add(
                new PlayerWardrobeOutfitEntity
                {
                    PlayerEntityId = _state.PlayerId.Value,
                    SlotId = slotId,
                    Figure = figure,
                    Gender = gender,
                }
            );
        }
        else
        {
            entity.Figure = figure;
            entity.Gender = gender;
        }

        await dbCtx.SaveChangesAsync(ct);

        await _events
            .PublishAsync(new WardrobeOutfitSavedEvent(_state.PlayerId, slotId, figure), ct)
            .ConfigureAwait(true);
    }
}
