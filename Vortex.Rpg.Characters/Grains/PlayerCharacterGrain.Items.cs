using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Database.Entities.Rpg;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Protocol.Messages.Outgoing.Rpg.Items;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Items;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>
/// The character's items (spec Objets §6, §7): the rules are <see cref="CharacterInventory"/>'s;
/// this part loads it, persists it and tells the rest of the server. A grant, a destroy and a
/// removal are written at once — what creates or ends value does not wait for a flush; moves,
/// splits and equipment are written by the item timer and at deactivation.
/// </summary>
internal sealed partial class PlayerCharacterGrain
{
    private CharacterInventory? _inventory;
    private long _itemIdsVersion;
    private IDisposable? _itemFlushTimer;

    public async Task<ItemGrantResult> GrantItemsAsync(string grantKey, ItemGrantLine[] items)
    {
        // The route validates the key; a blank or oversized one here is a caller's bug.
        if (
            string.IsNullOrWhiteSpace(grantKey)
            || grantKey.Length > RpgItemGrantEntity.GrantKeyLength
        )
        {
            throw new ArgumentException(
                $"A grant key is 1 to {RpgItemGrantEntity.GrantKeyLength} characters.",
                nameof(grantKey)
            );
        }

        if (await InventoryAsync() is not { } inventory)
        {
            return new ItemGrantResult { Outcome = ItemOutcome.NoCharacter };
        }

        if (await _itemStore.GrantKeySeenAsync(grantKey, CancellationToken.None))
        {
            return new ItemGrantResult { Outcome = ItemOutcome.DuplicateGrant };
        }

        // Placed on a copy, written with the key, and adopted only then: a write that throws
        // leaves the bag as it was and the key unused.
        CharacterInventory next = inventory.Clone();
        GrantPlan plan = next.Grant(items);

        if (plan.Outcome != ItemOutcome.Ok)
        {
            return new ItemGrantResult { Outcome = Logged("grant", plan.Outcome) };
        }

        try
        {
            await _itemStore.WriteAsync(_playerId, next.Dirty, grantKey, CancellationToken.None);
        }
        catch
        {
            // It may have committed all the same (a connection lost after the commit): read the
            // bag again rather than keep a memory a later flush would write over it.
            _inventory = null;

            throw;
        }

        next.MarkWritten();
        _inventory = next;

        foreach (ItemQuantity placed in plan.Placed)
        {
            Publish(
                new ItemGrantedEvent(
                    _playerId,
                    grantKey,
                    placed.Item.Id,
                    placed.Item.DefinitionId,
                    placed.Quantity,
                    placed.Item.Bound
                )
            );
        }

        logger.LogInformation(
            "Grant {GrantKey} to player {PlayerId}: {ToBag} to the bag, {ToParcels} to parcels.",
            grantKey,
            _playerId.Value,
            plan.ToBag,
            plan.ToParcels
        );

        // A server gesture: no result to answer, the bag as it now is.
        await PushAsync(InventoryComposer(next));
        TellTheZoneFacts();

        return new ItemGrantResult
        {
            Outcome = ItemOutcome.Ok,
            ToBag = plan.ToBag,
            ToParcels = plan.ToParcels,
        };
    }

    public async Task<ItemOutcome> RemoveItemsAsync(string definitionId, int quantity)
    {
        if (await InventoryAsync() is not { } inventory)
        {
            return ItemOutcome.NoCharacter;
        }

        // Written first, so every copy taken from has its id for the audit.
        await WriteNowAsync(inventory);

        CharacterInventory next = inventory.Clone();
        (ItemOutcome outcome, IReadOnlyList<ItemQuantity> taken) = next.Remove(
            definitionId,
            quantity
        );

        if (Logged("remove", outcome) != ItemOutcome.Ok)
        {
            return outcome;
        }

        await WriteNowAsync(next);
        _inventory = next;

        foreach (ItemQuantity t in taken)
        {
            Publish(new ItemRemovedEvent(_playerId, t.Item.Id, t.Item.DefinitionId, t.Quantity));
        }

        await PushAsync(InventoryComposer(next));
        TellTheZoneFacts();

        return outcome;
    }

    public async Task<ItemOutcome> MoveItemAsync(int from, int to) =>
        await InventoryAsync() is { } inventory
            ? await AnsweredAsync(
                RpgItemResultMessageComposer.ActionMove,
                Logged("move", inventory.Move(from, to))
            )
            : ItemOutcome.NoCharacter;

    public async Task<ItemOutcome> SplitItemAsync(int from, int quantity, int to) =>
        await InventoryAsync() is { } inventory
            ? await AnsweredAsync(
                RpgItemResultMessageComposer.ActionSplit,
                Logged("split", inventory.Split(from, quantity, to))
            )
            : ItemOutcome.NoCharacter;

    public async Task<ItemOutcome> ClaimParcelAsync(int parcel) =>
        await InventoryAsync() is { } inventory
            ? await AnsweredAsync(
                RpgItemResultMessageComposer.ActionClaim,
                Logged("claim", inventory.ClaimParcel(parcel))
            )
            : ItemOutcome.NoCharacter;

    public async Task<ItemOutcome> EquipItemAsync(int bagSlot)
    {
        if (await InventoryAsync() is not { } inventory || _character is not { } c)
        {
            return ItemOutcome.NoCharacter;
        }

        CharacterInventory next = inventory.Clone();
        ItemInstance? item = next.At(ItemContainer.Bag, bagSlot);
        bool wasBound = item?.Bound ?? false;
        ItemOutcome outcome = Logged("equip", next.Equip(bagSlot, c.ClassId, c.Level));

        if (outcome != ItemOutcome.Ok)
        {
            return await AnsweredAsync(RpgItemResultMessageComposer.ActionEquip, outcome);
        }

        bool boundNow = !wasBound && item!.Bound;

        // A binding is ownership: written before it counts, and audited with the copy's id.
        if (boundNow && !await WrittenAsync(next, "equip"))
        {
            return await AnsweredAsync(
                RpgItemResultMessageComposer.ActionEquip,
                ItemOutcome.Failed
            );
        }

        _inventory = next;

        if (boundNow)
        {
            Publish(new ItemBoundEvent(_playerId, item!.Id, item.DefinitionId));
        }

        await EquipmentChangedAsync();

        return await AnsweredAsync(RpgItemResultMessageComposer.ActionEquip, outcome);
    }

    public async Task<ItemOutcome> UnequipItemAsync(int equipmentSlot)
    {
        if (await InventoryAsync() is not { } inventory)
        {
            return ItemOutcome.NoCharacter;
        }

        ItemOutcome outcome = Logged("unequip", inventory.Unequip(equipmentSlot));

        if (outcome == ItemOutcome.Ok)
        {
            await EquipmentChangedAsync();
        }

        return await AnsweredAsync(RpgItemResultMessageComposer.ActionUnequip, outcome);
    }

    public async Task<ItemOutcome> DestroyItemAsync(int bagSlot, int quantity)
    {
        if (await InventoryAsync() is not { } inventory)
        {
            return ItemOutcome.NoCharacter;
        }

        // Written first, so the copy has its id for the audit, even a part split off since the
        // last flush; then the destroy on a copy, written before it counts.
        if (!await WrittenAsync(inventory, "destroy"))
        {
            return await AnsweredAsync(
                RpgItemResultMessageComposer.ActionDestroy,
                ItemOutcome.Failed
            );
        }

        CharacterInventory next = inventory.Clone();
        ItemInstance? item = next.At(ItemContainer.Bag, bagSlot);
        ItemOutcome outcome = Logged("destroy", next.Destroy(bagSlot, quantity));

        if (outcome != ItemOutcome.Ok)
        {
            return await AnsweredAsync(RpgItemResultMessageComposer.ActionDestroy, outcome);
        }

        if (!await WrittenAsync(next, "destroy"))
        {
            return await AnsweredAsync(
                RpgItemResultMessageComposer.ActionDestroy,
                ItemOutcome.Failed
            );
        }

        _inventory = next;
        Publish(new ItemDestroyedEvent(_playerId, item!.Id, item.DefinitionId, quantity));

        return await AnsweredAsync(RpgItemResultMessageComposer.ActionDestroy, outcome);
    }

    /// <summary>A player gesture's write, done now: false, logged, when the database refused it —
    /// the gesture is then answered <c>failed</c> rather than thrown at the socket.</summary>
    private async Task<bool> WrittenAsync(CharacterInventory inventory, string operation)
    {
        try
        {
            await WriteNowAsync(inventory);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Item {Operation} for player {PlayerId} failed: the write was refused.",
                operation,
                _playerId.Value
            );

            return false;
        }
    }

    /// <summary>The player is told how its operation ended, and given the bag after a change. A
    /// view only: a failure to send is logged and never undoes what changed.</summary>
    private async Task<ItemOutcome> AnsweredAsync(int action, ItemOutcome outcome)
    {
        RpgItemResultMessageComposer result = new()
        {
            Action = action,
            Code = JsonNamingPolicy.SnakeCaseLower.ConvertName(outcome.ToString()),
        };

        if (outcome == ItemOutcome.Ok && _inventory is { } inventory)
        {
            await PushAsync(result, InventoryComposer(inventory));
            // A destroy, a claimed parcel: what is owned may have moved (spec Z1 §4.3); a move or
            // an equip leaves it as it was, and tells nothing.
            TellTheZoneFacts();
        }
        else
        {
            await PushAsync(result);
        }

        return outcome;
    }

    private RpgInventoryMessageComposer InventoryComposer(CharacterInventory inventory) =>
        new()
        {
            BagSize = _itemConfig.BagSize,
            MaxParcels = _itemConfig.MaxParcels,
            Items = [.. inventory.Snapshot()],
        };

    public async Task<CharacterItemSnapshot[]> GetItemsAsync() =>
        (await InventoryAsync())?.Snapshot() ?? [];

    /// <summary>
    /// Null without a character. Loaded on first need; when the content version moved since, the
    /// definition ids are read again — an import's replace rule rewrites them in the table, and
    /// the flush never writes them back.
    /// </summary>
    private async Task<CharacterInventory?> InventoryAsync()
    {
        await RefreshIdsAsync();

        if (_character is null)
        {
            return null;
        }

        long version = content.Version;

        if (_inventory is null)
        {
            List<ItemInstance> items = await _itemStore.LoadAsync(
                _playerId,
                CancellationToken.None
            );

            foreach (ItemInstance unknown in items.Where(i => Definition(i.DefinitionId) is null))
            {
                // Readable rather than lost (spec Objets §11): it stays, and gives nothing.
                logger.LogWarning(
                    "Player {PlayerId} holds item {ItemId} of {DefinitionId}, unknown to the content.",
                    _playerId.Value,
                    unknown.Id,
                    unknown.DefinitionId
                );
            }

            _inventory = CharacterInventory.Load(
                new InventoryLimits(_itemConfig.BagSize, _itemConfig.MaxParcels),
                Definition,
                items
            );
            _itemIdsVersion = version;
        }
        else
        {
            await RefreshItemIdsAsync(_inventory);
        }

        return _inventory;
    }

    /// <summary>When the content version moved: the definition ids as an import left them in the
    /// table, onto the written copies and the parts split from them.</summary>
    private async Task RefreshItemIdsAsync(CharacterInventory inventory)
    {
        long version = content.Version;

        if (_itemIdsVersion == version)
        {
            return;
        }

        inventory.ReadDefinitionIds(
            await _itemStore.ReadDefinitionIdsAsync(_playerId, CancellationToken.None)
        );
        _itemIdsVersion = version;
        _stats = null;
    }

    /// <summary>What must be written before it counts: the dirty copies of
    /// <paramref name="inventory"/> now, an exception if the database refuses.</summary>
    private async Task WriteNowAsync(CharacterInventory inventory)
    {
        if (inventory.Dirty is not { Count: > 0 } dirty)
        {
            return;
        }

        try
        {
            await _itemStore.WriteAsync(_playerId, dirty, null, CancellationToken.None);
        }
        catch
        {
            // Committed or not, the table is the truth now: read again on the next call.
            _inventory = null;

            throw;
        }

        inventory.MarkWritten();
    }

    private ItemDefinition? Definition(string id) => content.Get<ItemDefinition>(id);

    /// <summary>The worn items changed: the stats are recomputed, announced and pushed.</summary>
    private async Task EquipmentChangedAsync()
    {
        _stats = null;
        Publish(new CharacterStatsChangedEvent(_playerId));
        await PushStatsAsync();
    }

    private ItemOutcome Logged(string operation, ItemOutcome outcome)
    {
        if (outcome != ItemOutcome.Ok)
        {
            logger.LogInformation(
                "Item {Operation} refused for player {PlayerId}: {Outcome}.",
                operation,
                _playerId.Value,
                outcome
            );
        }

        return outcome;
    }

    /// <summary>Writes the dirty items; a failure leaves them dirty for the next tick.</summary>
    private async Task FlushItemsAsync(CancellationToken ct)
    {
        if (_inventory is not { } inventory || inventory.Dirty is not { Count: > 0 } dirty)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();

        try
        {
            // A part split off before an import must not reach its row with a removed id.
            await RefreshItemIdsAsync(inventory);
            await _itemStore.WriteAsync(_playerId, dirty, null, ct);
            inventory.MarkWritten();
            metrics.ItemFlushCompleted(Stopwatch.GetElapsedTime(started).TotalMilliseconds, false);
        }
        catch (Exception ex)
        {
            metrics.ItemFlushCompleted(Stopwatch.GetElapsedTime(started).TotalMilliseconds, true);
            logger.LogWarning(
                ex,
                "Writing the items of player {PlayerId} failed; they stay dirty for the next flush.",
                _playerId.Value
            );
        }
    }
}
