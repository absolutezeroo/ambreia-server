using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Database.Entities.Rpg;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
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

        await _itemStore.WriteAsync(_playerId, next.Dirty, grantKey, CancellationToken.None);
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

        // So every copy taken from has its id for the audit.
        await FlushItemsAsync(CancellationToken.None);

        (ItemOutcome outcome, IReadOnlyList<ItemQuantity> taken) = inventory.Remove(
            definitionId,
            quantity
        );

        if (Logged("remove", outcome) != ItemOutcome.Ok)
        {
            return outcome;
        }

        await FlushItemsAsync(CancellationToken.None);

        foreach (ItemQuantity t in taken)
        {
            Publish(new ItemRemovedEvent(_playerId, t.Item.Id, t.Item.DefinitionId, t.Quantity));
        }

        return outcome;
    }

    public async Task<ItemOutcome> MoveItemAsync(int from, int to) =>
        await InventoryAsync() is { } inventory
            ? Logged("move", inventory.Move(from, to))
            : ItemOutcome.NoCharacter;

    public async Task<ItemOutcome> SplitItemAsync(int from, int quantity, int to) =>
        await InventoryAsync() is { } inventory
            ? Logged("split", inventory.Split(from, quantity, to))
            : ItemOutcome.NoCharacter;

    public async Task<ItemOutcome> ClaimParcelAsync(int parcel) =>
        await InventoryAsync() is { } inventory
            ? Logged("claim", inventory.ClaimParcel(parcel))
            : ItemOutcome.NoCharacter;

    public async Task<ItemOutcome> EquipItemAsync(int bagSlot)
    {
        if (await InventoryAsync() is not { } inventory || _character is not { } c)
        {
            return ItemOutcome.NoCharacter;
        }

        ItemInstance? item = inventory.At(ItemContainer.Bag, bagSlot);
        bool wasBound = item?.Bound ?? false;
        ItemOutcome outcome = Logged("equip", inventory.Equip(bagSlot, c.ClassId, c.Level));

        if (outcome != ItemOutcome.Ok)
        {
            return outcome;
        }

        if (!wasBound && item!.Bound)
        {
            // A binding is ownership: written now, and audited with the copy's id.
            await FlushItemsAsync(CancellationToken.None);
            Publish(new ItemBoundEvent(_playerId, item.Id, item.DefinitionId));
        }

        await EquipmentChangedAsync();

        return outcome;
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

        return outcome;
    }

    public async Task<ItemOutcome> DestroyItemAsync(int bagSlot, int quantity)
    {
        if (await InventoryAsync() is not { } inventory)
        {
            return ItemOutcome.NoCharacter;
        }

        // So the copy has its id for the audit, even a part split off since the last flush.
        await FlushItemsAsync(CancellationToken.None);

        ItemInstance? item = inventory.At(ItemContainer.Bag, bagSlot);
        ItemOutcome outcome = Logged("destroy", inventory.Destroy(bagSlot, quantity));

        if (outcome != ItemOutcome.Ok)
        {
            return outcome;
        }

        await FlushItemsAsync(CancellationToken.None);
        Publish(new ItemDestroyedEvent(_playerId, item!.Id, item.DefinitionId, quantity));

        return outcome;
    }

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
        else if (_itemIdsVersion != version)
        {
            Dictionary<int, string> ids = await _itemStore.ReadDefinitionIdsAsync(
                _playerId,
                CancellationToken.None
            );

            foreach (ItemInstance item in _inventory.Live)
            {
                if (item.Id > 0 && ids.TryGetValue(item.Id, out string? id))
                {
                    item.DefinitionId = id;
                }
            }

            _itemIdsVersion = version;
            _stats = null;
        }

        return _inventory;
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
