using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Content;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;

namespace Vortex.Dashboard.API.Admin.Hotel;

/// <summary>
/// The hotel's smaller editable tables: hand items, bots, the currency/rental rows,
/// and direct player grants.
/// <para>
/// Three different live-state rules apply here, and each one is enforced rather than assumed. Hand
/// items are read from the database every time a pet is fed, so a write is live immediately. A bot
/// standing in a room is owned by that room's grain, so editing its row would leave the two
/// disagreeing — those edits are refused until it is picked up. Player grants go through the
/// player's own grain where one exists, so the client is told rather than silently drifting.
/// </para>
/// </summary>
internal sealed partial class ContentAdminService
{
    public async Task<ContentAdminResult> UpsertHandItemAsync(
        HandItemSpec spec,
        CancellationToken ct
    )
    {
        if (spec.HandItemId <= 0)
        {
            return ContentAdminResult.Fail("hand_item_id_required");
        }

        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            return ContentAdminResult.Fail("name_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        HandItemEntity? entity = await db
            .HandItems.FirstOrDefaultAsync(h => h.HandItemId == spec.HandItemId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new HandItemEntity
            {
                HandItemId = spec.HandItemId,
                Name = spec.Name.Trim(),
                Nutrition = Math.Max(0, spec.Nutrition),
                Thirst = Math.Max(0, spec.Thirst),
            };

            db.HandItems.Add(entity);
        }
        else
        {
            entity.Name = spec.Name.Trim();
            entity.Nutrition = Math.Max(0, spec.Nutrition);
            entity.Thirst = Math.Max(0, spec.Thirst);
            entity.DeletedAt = null;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(entity.Id);
    }

    public async Task<ContentAdminResult> DeleteHandItemAsync(int handItemId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        HandItemEntity? entity = await db
            .HandItems.FirstOrDefaultAsync(h => h.Id == handItemId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("hand_item_not_found");
        }

        db.HandItems.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(handItemId);
    }

    public async Task<ContentAdminResult> UpdateBotAsync(
        int botId,
        BotSpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            return ContentAdminResult.Fail("name_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        BotEntity? entity = await db
            .Bots.FirstOrDefaultAsync(b => b.Id == botId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("bot_not_found");
        }

        if (entity.RoomEntityId is not null)
        {
            // A placed bot is owned by its room's grain, which holds its own copy of the name,
            // figure and position. Writing the row underneath would leave the two disagreeing until
            // the room reloads, so the edit waits until the bot is back in its owner's hand.
            return ContentAdminResult.Fail("bot_is_placed");
        }

        entity.Name = spec.Name.Trim();
        entity.Motto = spec.Motto?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(spec.Figure))
        {
            entity.Figure = spec.Figure.Trim();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(entity.Id);
    }

    public async Task<ContentAdminResult> DeleteBotAsync(int botId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        BotEntity? entity = await db
            .Bots.FirstOrDefaultAsync(b => b.Id == botId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("bot_not_found");
        }

        if (entity.RoomEntityId is not null)
        {
            return ContentAdminResult.Fail("bot_is_placed");
        }

        db.Bots.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(botId);
    }

    public async Task<ContentAdminResult> CreateCurrencyAsync(
        CurrencySpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            return ContentAdminResult.Fail("name_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CurrencyTypeEntity entity = new()
        {
            Name = spec.Name.Trim(),
            CurrencyType = (Primitives.Players.Enums.Wallet.CurrencyType)spec.CurrencyType,
            ActivityPointType = spec.ActivityPointType,
            Enabled = spec.Enabled,
            StartingAmount = Math.Max(0, spec.StartingAmount),
        };

        db.CurrencyTypes.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadCurrenciesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(entity.Id);
    }

    public async Task<ContentAdminResult> UpdateCurrencyAsync(
        int currencyId,
        CurrencySpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            return ContentAdminResult.Fail("name_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CurrencyTypeEntity? entity = await db
            .CurrencyTypes.FirstOrDefaultAsync(c => c.Id == currencyId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("currency_not_found");
        }

        entity.Name = spec.Name.Trim();
        entity.CurrencyType = (Primitives.Players.Enums.Wallet.CurrencyType)spec.CurrencyType;
        entity.ActivityPointType = spec.ActivityPointType;
        entity.Enabled = spec.Enabled;
        entity.StartingAmount = Math.Max(0, spec.StartingAmount);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadCurrenciesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(entity.Id);
    }

    public async Task<ContentAdminResult> GrantBadgeAsync(
        int playerId,
        string badgeCode,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(badgeCode))
        {
            return ContentAdminResult.Fail("badge_code_required");
        }

        string code = badgeCode.Trim();

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerEntity? player = await db
            .Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            .ConfigureAwait(false);

        if (player is null)
        {
            return ContentAdminResult.Fail("player_not_found");
        }

        PlayerBadgeEntity? existing = await db
            .PlayerBadges.FirstOrDefaultAsync(
                b => b.PlayerEntityId == playerId && b.BadgeCode == code,
                ct
            )
            .ConfigureAwait(false);

        if (existing is not null && existing.DeletedAt is null)
        {
            return ContentAdminResult.Fail("badge_already_held");
        }

        if (existing is not null)
        {
            existing.DeletedAt = null;
        }
        else
        {
            db.PlayerBadges.Add(
                new PlayerBadgeEntity
                {
                    PlayerEntityId = playerId,
                    PlayerEntity = player,
                    BadgeCode = code,
                }
            );
        }

        // The badge grain re-reads the table on every request rather than caching, so the row is the
        // whole grant — no reload to chase.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(playerId);
    }

    public async Task<ContentAdminResult> RevokeBadgeAsync(
        int playerId,
        string badgeCode,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerBadgeEntity? entity = await db
            .PlayerBadges.FirstOrDefaultAsync(
                b => b.PlayerEntityId == playerId && b.BadgeCode == badgeCode,
                ct
            )
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("badge_not_held");
        }

        // Same as the grant: the badge grain re-reads the table per request rather than caching, so
        // removing the row is the whole revocation and there is no live copy to chase.
        db.PlayerBadges.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(playerId);
    }

    public async Task<ContentAdminResult> GrantEffectAsync(
        int playerId,
        int effectId,
        int durationSeconds,
        CancellationToken ct
    )
    {
        if (effectId <= 0)
        {
            return ContentAdminResult.Fail("effect_id_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        if (!await db.Players.AnyAsync(p => p.Id == playerId, ct).ConfigureAwait(false))
        {
            return ContentAdminResult.Fail("player_not_found");
        }

        // Through the grain, not the table: it is what pushes AvatarEffectAdded, so a granted effect
        // appears in the client's list without a reconnect.
        await grainFactory
            .GetPlayerEffectGrain((PlayerId)playerId)
            .AddEffectAsync(effectId, 0, Math.Max(0, durationSeconds), ct)
            .ConfigureAwait(false);

        return ContentAdminResult.Ok(playerId);
    }

    public async Task<ContentAdminResult> RevokeEffectAsync(
        int playerId,
        int effectId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerEffectEntity? entity = await db
            .PlayerEffects.FirstOrDefaultAsync(
                e => e.PlayerEntityId == playerId && e.EffectId == effectId && e.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ContentAdminResult.Fail("effect_not_owned");
        }

        // Not symmetric with the grant above, and deliberately so for now: PlayerEffectGrain caches
        // nothing -- every one of its methods opens a context and reads the table -- so deleting the
        // row IS the revocation as far as the server is concerned.
        //
        // The connected client is the part that goes stale. The grant pushes AvatarEffectAdded;
        // there is no removal composer and no grain method to route this through, so a player who is
        // online keeps the effect in their list, and keeps wearing it if it was selected, until they
        // reconnect. Closing that gap means a composer and a grain method, which is a protocol
        // change rather than an admin one.
        db.PlayerEffects.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(playerId);
    }

    /// <summary>
    /// Moves one item to another player.
    /// </summary>
    /// <remarks>
    /// Refused while the item is in a room for the same reason the delete is: the room owns a
    /// placed item, and handing it to someone else underneath would leave the room showing it to
    /// the wrong name. Both inventories are reloaded -- the one losing it and the one gaining it --
    /// because each grain caches its own list and only one of them is obvious to remember.
    /// </remarks>
    public async Task<ContentAdminResult> TransferFurnitureAsync(
        int playerId,
        int toPlayerId,
        int itemId,
        CancellationToken ct
    )
    {
        if (playerId == toPlayerId)
        {
            return ContentAdminResult.Fail("same_player");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        FurnitureEntity? item = await db
            .Furnitures.FirstOrDefaultAsync(f => f.Id == itemId, ct)
            .ConfigureAwait(false);

        if (item is null)
        {
            return ContentAdminResult.Fail("item_not_found");
        }

        if (item.PlayerEntityId != playerId)
        {
            return ContentAdminResult.Fail("item_not_owned");
        }

        if (item.RoomEntityId is not null)
        {
            return ContentAdminResult.Fail("item_is_placed");
        }

        bool recipientExists = await db
            .Players.AnyAsync(p => p.Id == toPlayerId, ct)
            .ConfigureAwait(false);

        if (!recipientExists)
        {
            return ContentAdminResult.Fail("recipient_not_found");
        }

        item.PlayerEntityId = toPlayerId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await ReloadInventoryAsync(playerId, ct).ConfigureAwait(false);
        await ReloadInventoryAsync(toPlayerId, ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(itemId);
    }

    /// <summary>
    /// Takes one item back and pays for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Paid at what the catalogue asks <em>today</em>, not at what the player paid. The price they
    /// paid is recoverable only when the purchase is still in the ledger and still correlated, which
    /// is not true for an item that has been traded, won or granted -- and a refund that silently
    /// pays zero in those cases would be worse than one that refuses.
    /// </para>
    /// <para>
    /// So an item the catalogue does not sell is refused rather than guessed at, and only credits
    /// are paid: an offer priced in another currency is a decision about which wallet to top up, and
    /// that belongs to whoever asks for it, not to a default chosen here.
    /// </para>
    /// </remarks>
    public async Task<ContentAdminResult> RefundFurnitureAsync(
        int playerId,
        int itemId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        FurnitureEntity? item = await db
            .Furnitures.FirstOrDefaultAsync(f => f.Id == itemId, ct)
            .ConfigureAwait(false);

        if (item is null)
        {
            return ContentAdminResult.Fail("item_not_found");
        }

        if (item.PlayerEntityId != playerId)
        {
            return ContentAdminResult.Fail("item_not_owned");
        }

        if (item.RoomEntityId is not null)
        {
            return ContentAdminResult.Fail("item_is_placed");
        }

        // The cheapest offer that sells this furniture, so a definition on a discounted page does
        // not pay out at the price of a bundle that happens to contain it.
        int? price = await db
            .CatalogProducts.AsNoTracking()
            .Where(p => p.FurnitureDefinitionEntityId == item.FurnitureDefinitionEntityId)
            .Select(p => (int?)p.Offer.CostCredits)
            .Where(cost => cost > 0)
            .OrderBy(cost => cost)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (price is null or <= 0)
        {
            return ContentAdminResult.Fail("no_catalogue_price");
        }

        db.Furnitures.Remove(item);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await ReloadInventoryAsync(playerId, ct).ConfigureAwait(false);

        // After the row is gone: paying first and then failing to remove the item is the one
        // ordering that hands out a free duplicate.
        await grainFactory
            .GetPlayerWalletGrain(new PlayerId(playerId))
            .GrantCreditsAsync(price.Value, ct)
            .ConfigureAwait(false);

        return ContentAdminResult.Ok(price.Value);
    }

    /// <summary>
    /// Re-reads one player's furniture into their inventory grain.
    /// </summary>
    /// <remarks>
    /// The list is a cache built at activation, so a row that changed underneath is invisible until
    /// this runs. Every item write here ends with it, which is why it is one method rather than a
    /// line each of them has to remember.
    /// </remarks>
    private Task ReloadInventoryAsync(int playerId, CancellationToken ct) =>
        grainFactory.GetInventoryGrain(new PlayerId(playerId)).ReloadFurnitureAsync(ct);

    /// <summary>
    /// Take one item off a player, permanently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of the item grant, which had none: an operator could hand out furniture and
    /// never take it back, so a mistaken grant needed SQL.
    /// </para>
    /// <para>
    /// Refused while the item sits in a room. A placed item belongs to that room's live state, and
    /// deleting the row underneath it leaves the room holding an object that no longer exists --
    /// the same reason <see cref="DeleteBotAsync"/> refuses with <c>bot_is_placed</c>. Picking it
    /// up first is one click for the owner and keeps a single writer over the item.
    /// </para>
    /// <para>
    /// The inventory grain caches the furniture list from activation, so the row going is only half
    /// the work: without the reload the player keeps seeing what they owned a moment ago. That is
    /// the same call the trade path makes after handing items back.
    /// </para>
    /// </remarks>
    public async Task<ContentAdminResult> RevokeFurnitureAsync(
        int playerId,
        int itemId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        FurnitureEntity? item = await db
            .Furnitures.FirstOrDefaultAsync(f => f.Id == itemId, ct)
            .ConfigureAwait(false);

        if (item is null)
        {
            return ContentAdminResult.Fail("item_not_found");
        }

        // The owner is checked rather than trusted: the id alone would let a typo take a stranger's
        // furniture, and the audit entry would name the wrong victim.
        if (item.PlayerEntityId != playerId)
        {
            return ContentAdminResult.Fail("item_not_owned");
        }

        if (item.RoomEntityId is not null)
        {
            return ContentAdminResult.Fail("item_is_placed");
        }

        db.Furnitures.Remove(item);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await ReloadInventoryAsync(playerId, ct).ConfigureAwait(false);

        return ContentAdminResult.Ok(itemId);
    }

    private async Task ReloadCurrenciesAsync(CancellationToken ct)
    {
        try
        {
            await currencyTypes.ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Currency reference data reload failed after an admin write committed -- the live currency list is stale until the next reload or restart"
            );
            throw;
        }
    }
}
