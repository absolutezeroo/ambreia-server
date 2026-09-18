using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Inventory.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Outgoing.Inventory.Badges;
using Vortex.Protocol.Messages.Outgoing.Inventory.Furni;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.Players.Grains.Modules;

internal sealed class PlayerInventoryModule(PlayerPresenceGrain presenceGrain)
{
    private readonly PlayerPresenceGrain _presenceGrain = presenceGrain;

    public async Task OpenFurnitureInventoryAsync(CancellationToken ct)
    {
        IInventoryGrain inventoryGrain = _presenceGrain._grainFactory.GetInventoryGrain(
            _presenceGrain.GetPrimaryKeyLong()
        );
        ImmutableArray<FurnitureItemSnapshot> items = await inventoryGrain.GetAllItemSnapshotsAsync(
            ct
        );
        int furniPerFragment = _presenceGrain._config.FurniInventoryFragmentSize;

        int totalFragments = (int)
            Math.Max(1, Math.Ceiling((double)items.Length / furniPerFragment));
        int currentFragment = 0;
        int count = 0;
        List<FurnitureItemSnapshot> fragmentItems = [];

        foreach (FurnitureItemSnapshot item in items)
        {
            fragmentItems.Add(item);

            count++;

            if (count == furniPerFragment)
            {
                await _presenceGrain.SendComposerAsync(
                    new FurniListEventMessageComposer
                    {
                        TotalFragments = totalFragments,
                        CurrentFragment = currentFragment,
                        Items = [.. fragmentItems],
                    }
                );

                fragmentItems.Clear();
                count = 0;
                currentFragment++;
            }
        }

        await _presenceGrain.SendComposerAsync(
            new FurniListEventMessageComposer
            {
                TotalFragments = totalFragments,
                CurrentFragment = currentFragment,
                Items = [.. fragmentItems],
            }
        );
    }

    /// <summary>
    /// Hands the client a newly arrived item and tells it the item has not been looked at, which is
    /// what puts the dot on the inventory tab.
    /// </summary>
    /// <remarks>
    /// Both, in this order, is what the official server sends on a purchase (capture 2026-09-18).
    /// The order is cosmetic -- the client's UnseenItemTracker files ids in a plain dictionary and
    /// never asks whether it knows the item yet -- but matching it costs nothing.
    ///
    /// The unseen mark lives only in the client from here on. We keep no server-side set, so a
    /// reconnect forgets it, and that is why the two ResetUnseenItem handlers have nothing to do.
    /// Persisting it would take a store of its own plus a push at login, and no capture shows what
    /// the official server sends there.
    /// </remarks>
    public async Task OnFurnitureAddedAsync(FurnitureItemSnapshot snapshot, CancellationToken ct)
    {
        await _presenceGrain
            .SendComposerAsync(
                new UnseenItemsEventMessageComposer
                {
                    Categories =
                    [
                        new UnseenItemCategory
                        {
                            // An item that expires is one the player is renting, and the client
                            // files those under their own tab.
                            CategoryId = (int)(
                                snapshot.SecondsToExpiration > 0
                                    ? InventoryCategory.RentedFurni
                                    : InventoryCategory.OwnedFurni
                            ),
                            // The same id the furniture list leads with -- that is the one the
                            // client keys its tracker on, not the signed copy further in.
                            ItemIds = [snapshot.ItemId],
                        },
                    ],
                }
            )
            .ConfigureAwait(true);

        await _presenceGrain
            .SendComposerAsync(new FurniListAddOrUpdateEventMessageComposer { Item = snapshot })
            .ConfigureAwait(true);
    }

    public Task OnFurnitureRemovedAsync(RoomObjectId itemId, CancellationToken ct) =>
        _presenceGrain.SendComposerAsync(
            new FurniListRemoveEventMessageComposer { ItemId = itemId }
        );

    public Task OnFurnitureListInvalidatedAsync(CancellationToken ct) =>
        _presenceGrain.SendComposerAsync(new FurniListInvalidateEventMessageComposer());

    public Task OnBadgeGrantedAsync(string badgeCode, CancellationToken ct) =>
        _presenceGrain.SendComposerAsync(
            new BadgeReceivedEventMessageComposer { BadgeCode = badgeCode }
        );

    public Task OnPetAddedToInventoryAsync(PetSnapshot pet, CancellationToken ct) =>
        _presenceGrain.SendComposerAsync(new PetAddedToInventoryEventMessageComposer { Pet = pet });
}
