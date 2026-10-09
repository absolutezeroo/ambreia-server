using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Primitives.Players;
using Vortex.Rpg.Items;

namespace Vortex.Rpg.Characters.Grains.Persistence;

/// <summary>
/// The only code that reads and writes <c>rpg_items</c> and <c>rpg_item_grants</c> (spec Objets
/// §5, §6), for <see cref="PlayerCharacterGrain"/>. A write is one <c>SaveChanges</c>, so one
/// transaction: a grant's key and its items land together or not at all.
/// </summary>
internal sealed class CharacterItemStore(IDbContextFactory<VortexDbContext> dbFactory)
{
    public async Task<List<ItemInstance>> LoadAsync(PlayerId player, CancellationToken ct)
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        return await db
            .RpgItems.AsNoTracking()
            .Where(i => i.PlayerEntityId == player.Value && i.DeletedAt == null)
            .Select(i => new ItemInstance
            {
                Id = i.Id,
                DefinitionId = i.DefinitionId,
                Quantity = i.Quantity,
                Durability = i.Durability,
                Bound = i.Bound,
                Container = i.Container,
                Slot = i.Slot,
            })
            .ToListAsync(ct);
    }

    public async Task<bool> GrantKeySeenAsync(string grantKey, CancellationToken ct)
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        return await db.RpgItemGrants.AnyAsync(g => g.GrantKey == grantKey, ct);
    }

    /// <summary>The definition id of every live copy, as an import's replace rule left it.</summary>
    public async Task<Dictionary<int, string>> ReadDefinitionIdsAsync(
        PlayerId player,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);

        return await db
            .RpgItems.AsNoTracking()
            .Where(i => i.PlayerEntityId == player.Value && i.DeletedAt == null)
            .ToDictionaryAsync(i => i.Id, i => i.DefinitionId, ct);
    }

    /// <summary>
    /// Writes <paramref name="dirty"/>, and <paramref name="grantKey"/> when given, in one
    /// transaction; new copies get their ids back. A copy deleted before it was ever written has
    /// no row and is skipped.
    /// </summary>
    public async Task WriteAsync(
        PlayerId player,
        IReadOnlyList<ItemInstance> dirty,
        string? grantKey,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<(ItemInstance Item, RpgItemEntity Row)> added = [];

        foreach (ItemInstance item in dirty)
        {
            if (item.Id == 0)
            {
                if (!item.Deleted)
                {
                    RpgItemEntity row = ToRow(player, item);
                    db.RpgItems.Add(row);
                    added.Add((item, row));
                }

                continue;
            }

            RpgItemEntity existing = ToRow(player, item);
            existing.Id = item.Id;
            existing.DeletedAt = item.Deleted ? DateTime.UtcNow : null;
            EntityEntry<RpgItemEntity> entry = db.RpgItems.Attach(existing);

            // definition_id is never written from memory: an import's replace rule owns it
            // (AGENTS.md, Gotchas).
            entry.Property(r => r.Quantity).IsModified = true;
            entry.Property(r => r.Durability).IsModified = true;
            entry.Property(r => r.Bound).IsModified = true;
            entry.Property(r => r.Container).IsModified = true;
            entry.Property(r => r.Slot).IsModified = true;
            entry.Property(r => r.DeletedAt).IsModified = true;
        }

        if (grantKey is not null)
        {
            db.RpgItemGrants.Add(
                new RpgItemGrantEntity { GrantKey = grantKey, PlayerEntityId = player.Value }
            );
        }

        await db.SaveChangesAsync(ct);

        foreach ((ItemInstance item, RpgItemEntity row) in added)
        {
            item.Id = row.Id;
        }
    }

    private static RpgItemEntity ToRow(PlayerId player, ItemInstance item) =>
        new()
        {
            PlayerEntityId = player.Value,
            DefinitionId = item.DefinitionId,
            Quantity = item.Quantity,
            Durability = item.Durability,
            Bound = item.Bound,
            Container = item.Container,
            Slot = item.Slot,
        };
}
