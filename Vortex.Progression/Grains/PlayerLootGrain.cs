using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Loot;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Loot.Grains;
using Vortex.Primitives.Loot.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Progression.Grains;

/// <summary>
/// Grants drawn prizes to one player. A stateless gateway grain: it owns no data of its own, it
/// routes a pool entry to whichever grain actually owns the thing being handed out (inventory,
/// effects, club) and raises the audit event.
///
/// Every reward furniture ends here — the mystery box today, crackables and reward boxes next — so a
/// new trigger gets both the grant semantics and the audit trail without reimplementing either. The
/// mystery trophy is the deliberate exception: its prize carries an inscription baked into the
/// furniture's stuff data, which no other prize has.
/// </summary>
internal sealed class PlayerLootGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IFurnitureDefinitionProvider furnitureDefinitionProvider,
    IEventPublisher events,
    ILogger<PlayerLootGrain> logger
) : Grain, IPlayerLootGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IFurnitureDefinitionProvider _furnitureDefinitionProvider =
        furnitureDefinitionProvider;
    private readonly IEventPublisher _events = events;
    private readonly ILogger<PlayerLootGrain> _logger = logger;

    private int OwnerId => (int)this.GetPrimaryKeyLong();

    public async Task<LootAward?> GrantAsync(
        LootEntrySnapshot entry,
        string source,
        CancellationToken ct
    )
    {
        LootAward? award;

        try
        {
            award = entry.ProductType switch
            {
                ProductType.Floor or ProductType.Wall => await GrantFurnitureAsync(entry, ct)
                    .ConfigureAwait(true),
                ProductType.Effect => await GrantEffectAsync(entry, ct).ConfigureAwait(true),
                ProductType.Premium => await GrantMembershipAsync(entry, ct).ConfigureAwait(true),
                _ => LogUngrantableLoot(entry),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to grant prize {EntryId} from pool '{PoolCode}' to player {PlayerId}",
                entry.Id,
                entry.PoolCode,
                OwnerId
            );

            return null;
        }

        if (award is null)
        {
            return null;
        }

        // Raised here rather than by the caller: a trigger that forgot would leave a real payout off
        // the trail, and the per-pool payout history is what an operator checks a disputed prize
        // against.
        await _events
            .PublishAsync(
                new LootAwardedEvent(
                    OwnerId,
                    entry.PoolCode,
                    entry.Id,
                    entry.Variant,
                    award.ContentType,
                    award.ClassId,
                    source
                ),
                ct
            )
            .ConfigureAwait(true);

        return award;
    }

    public async Task<LootAward?> GrantOnceAsync(
        LootEntrySnapshot entry,
        int poolId,
        string source,
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            // Read-then-write is safe here without a transaction: this grain is the only writer for
            // this player and Orleans runs its turns one at a time, so two clicks queue rather than
            // interleave. The unique index still backs it up across silos.
            bool alreadyClaimed = await dbCtx
                .PlayerLootClaims.AsNoTracking()
                .AnyAsync(c => c.PlayerEntityId == OwnerId && c.LootTableEntityId == poolId, ct)
                .ConfigureAwait(true);

            if (alreadyClaimed)
            {
                return null;
            }

            LootAward? award = await GrantAsync(entry, source, ct).ConfigureAwait(true);

            if (award is null)
            {
                // Nothing was handed over, so nothing is claimed: the player keeps their one shot
                // rather than losing it to a broken prize row.
                return null;
            }

            dbCtx.PlayerLootClaims.Add(
                new PlayerLootClaimEntity { PlayerEntityId = OwnerId, LootTableEntityId = poolId }
            );

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            return award;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record the once-per-player claim on pool {PoolId} for player {PlayerId}",
                poolId,
                OwnerId
            );

            return null;
        }
    }

    private LootAward? LogUngrantableLoot(LootEntrySnapshot entry)
    {
        _logger.LogWarning(
            "Loot {EntryId} has product type {ProductType}, which cannot be granted; player {PlayerId} received nothing.",
            entry.Id,
            entry.ProductType,
            OwnerId
        );

        return null;
    }

    private async Task<LootAward?> GrantFurnitureAsync(
        LootEntrySnapshot entry,
        CancellationToken ct
    )
    {
        FurnitureDefinitionSnapshot? definition = _furnitureDefinitionProvider.TryGetDefinition(
            entry.FurnitureDefinitionId
        );

        if (definition is null)
        {
            _logger.LogWarning(
                "Loot {EntryId} points at furniture definition {DefinitionId}, which does not exist; player {PlayerId} received nothing.",
                entry.Id,
                entry.FurnitureDefinitionId,
                OwnerId
            );

            return null;
        }

        await _grainFactory
            .GetInventoryGrain(OwnerId)
            .GrantFurnitureDefinitionAsync(definition.Id, null, ct)
            .ConfigureAwait(true);

        // The reward window resolves furniture artwork by sprite id, not by our definition id.
        return new LootAward
        {
            ContentType = definition.ProductType.ToLegacyString(),
            ClassId = definition.SpriteId,
        };
    }

    private async Task<LootAward?> GrantEffectAsync(LootEntrySnapshot entry, CancellationToken ct)
    {
        // Same encoding the catalog uses: "effectId", "effectId:durationSeconds" or
        // "effectId:durationSeconds:subType" (duration 0/absent = permanent).
        string[] parts = entry.ExtraParam.Split(':');

        if (
            parts.Length == 0
            || !int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int effectId
            )
            || effectId <= 0
        )
        {
            _logger.LogWarning(
                "Loot {EntryId} has effect parameters '{ExtraParam}', which do not name an effect; player {PlayerId} received nothing.",
                entry.Id,
                entry.ExtraParam,
                OwnerId
            );

            return null;
        }

        int duration =
            parts.Length > 1
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)
                ? d
                : 0;
        int subType =
            parts.Length > 2
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)
                ? s
                : 0;

        await _grainFactory
            .GetPlayerEffectGrain(OwnerId)
            .AddEffectAsync(effectId, subType, duration, ct)
            .ConfigureAwait(true);

        return new LootAward
        {
            ContentType = ProductType.Effect.ToLegacyString(),
            ClassId = effectId,
        };
    }

    private async Task<LootAward?> GrantMembershipAsync(
        LootEntrySnapshot entry,
        CancellationToken ct
    )
    {
        // ExtraParam is the number of months, optionally suffixed with ":vip".
        string[] parts = entry.ExtraParam.Split(':');

        if (
            parts.Length == 0
            || !int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int months
            )
            || months <= 0
        )
        {
            _logger.LogWarning(
                "Loot {EntryId} has club parameters '{ExtraParam}', which do not name a month count; player {PlayerId} received nothing.",
                entry.Id,
                entry.ExtraParam,
                OwnerId
            );

            return null;
        }

        bool isVip = parts.Length > 1 && parts[1].Equals("vip", StringComparison.OrdinalIgnoreCase);

        MembershipPurchaseResult result = await _grainFactory
            .GetPlayerGrain(OwnerId)
            .GrantMembershipMonthsAsync(months, isVip, CommerceOperationId.None, ct)
            .ConfigureAwait(true);

        if (result != MembershipPurchaseResult.Success)
        {
            _logger.LogWarning(
                "Loot {EntryId} failed to extend club membership for player {PlayerId}: {Result}",
                entry.Id,
                OwnerId,
                result
            );

            return null;
        }

        // The club reward icon is looked up by product id; the entry's definition id doubles as that
        // id so an operator can point it at whichever club product artwork they ship.
        return new LootAward
        {
            ContentType = ProductType.Premium.ToLegacyString(),
            ClassId = entry.FurnitureDefinitionId,
        };
    }
}
