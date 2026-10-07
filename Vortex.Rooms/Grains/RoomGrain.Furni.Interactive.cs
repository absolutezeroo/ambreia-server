using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Primitives.Action;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Furniture;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Rooms.Object.Logic.Furniture.Wall;

namespace Vortex.Rooms.Grains;

/// <summary>
/// Furniture whose interesting state is data rather than a state number: a mannequin's outfit, a
/// sticky note's text. All of it goes through the same three steps — find the item, check the actor
/// may build here, write and persist — so they are kept together rather than spread across the
/// individual widget handlers.
/// </summary>
public sealed partial class RoomGrain
{
    private const string MannequinGenderKey = "GENDER";
    private const string MannequinFigureKey = "FIGURE";
    private const string MannequinNameKey = "OUTFIT_NAME";

    /// <summary>
    /// Resolves an item the actor is allowed to reconfigure. Rights, not ownership: a room owner
    /// expects to be able to fix a mannequin a visitor left in their room, and Habbo has always
    /// worked that way.
    /// </summary>
    private async Task<IRoomItem?> FindManipulableItemAsync(ActionContext ctx, RoomObjectId itemId)
    {
        if (!_state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
        {
            return null;
        }

        return await SecurityModule.CanManipulateFurniAsync(ctx).ConfigureAwait(true) ? item : null;
    }

    public async Task<bool> SetMannequinOutfitAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        string figure,
        string gender,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        if (item?.Logic.StuffData is not IMapStuffData map || string.IsNullOrWhiteSpace(figure))
        {
            return false;
        }

        map.Data[MannequinFigureKey] = figure;
        map.Data[MannequinGenderKey] = gender;

        // A mannequin that has never been named keeps whatever the client last showed, so the key
        // is created empty rather than left absent — the widget reads it unconditionally.
        _ = map.Data.TryAdd(MannequinNameKey, string.Empty);

        await item.Logic.PersistStuffDataAsync().ConfigureAwait(true);

        return true;
    }

    public async Task<bool> SetMannequinNameAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        string name,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        if (item?.Logic.StuffData is not IMapStuffData map)
        {
            return false;
        }

        map.Data[MannequinNameKey] = name;

        await item.Logic.PersistStuffDataAsync().ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// The widget's "back to normal" button, and the only value it sends that is not a height.
    /// </summary>
    private const int ResetStackHeightSentinel = -100;

    public async Task<bool> SetCustomStackHeightAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int heightHundredths,
        bool? multiWalk,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        // Gated on the logic, not on the packet: any client can name any object id here, and moving
        // an arbitrary furni to an arbitrary altitude is exactly what the magic tile is allowed to
        // do and nothing else is.
        if (
            item is not IRoomFloorItem floor
            || floor.Logic is not FurnitureCustomStackHeightLogic logic
        )
        {
            return false;
        }

        // Sent on its own when the checkbox is what moved, so it is applied before the height is
        // range-checked — otherwise ticking the box at an out-of-range height would silently drop it.
        if (multiWalk is not null)
        {
            logic.SetMultiWalk(multiWalk.Value);
        }

        bool reset = heightHundredths == ResetStackHeightSentinel;

        if (
            !reset
            && (heightHundredths < 0 || heightHundredths > _roomConfig.MaxStackHeight.ToInt())
        )
        {
            return false;
        }

        int tileIdx = MapModule.ToIdx(floor.X, floor.Y);

        if (!MapModule.InBounds(tileIdx))
        {
            return false;
        }

        if (reset)
        {
            // Taken out of the stack first so the tile height recomputes without it — asking for
            // the tile's current height while the tile is still the thing defining it would just
            // hand back the height being cleared.
            MapModule.RemoveFloorItem(floor);
            MapModule.PlaceFloorItem(floor, tileIdx, floor.Rotation);
        }
        else
        {
            MapModule.MoveFloorItem(floor, tileIdx, Altitude.FromInt(heightHundredths));
        }

        floor.MarkDirty();

        await SendComposerToRoomAsync(floor.GetUpdateComposer()).ConfigureAwait(true);

        return true;
    }

    public async Task<bool> SetAdjacentCustomStackHeightAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        bool down,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        if (item is not IRoomFloorItem floor || floor.Logic is not FurnitureCustomStackHeightLogic)
        {
            return false;
        }

        int tileIdx = MapModule.ToIdx(floor.X, floor.Y);

        if (!MapModule.InBounds(tileIdx))
        {
            return false;
        }

        double? target = AdjacentStackHeight.Pick(
            floor.Z,
            SurfacesOnTile(tileIdx, itemId),
            down,
            _roomConfig.MaxStackHeight
        );

        if (target is null)
        {
            return false;
        }

        MapModule.MoveFloorItem(floor, tileIdx, Altitude.FromValue(target.Value));
        floor.MarkDirty();

        await SendComposerToRoomAsync(floor.GetUpdateComposer()).ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// Every height an item on this tile could be snapped to: both edges of everything else
    /// standing here, plus the floor. An item's underside is as much a surface to land on as its
    /// top, and <paramref name="movingItemId"/> is left out — its own two edges would otherwise be
    /// the nearest candidates and the arrow would never move anything.
    /// </summary>
    private IEnumerable<double> SurfacesOnTile(int tileIdx, RoomObjectId movingItemId)
    {
        foreach (RoomObjectId stackedId in _state.TileFloorStacks[tileIdx])
        {
            if (
                stackedId == movingItemId
                || !_state.ItemsById.TryGetValue(stackedId, out IRoomItem? stacked)
                || stacked is not IRoomFloorItem stackedFloor
            )
            {
                continue;
            }

            yield return stackedFloor.Z;
            yield return stackedFloor.Z + stackedFloor.GetStackHeight();
        }

        yield return _state.Model?.BaseHeights[tileIdx] ?? Altitude.Zero;
    }

    /// <summary>
    /// Removes furniture that has been used up — a cracked egg, an opened present — from the room
    /// and the database. Returns false when the delete did not happen, so the caller can avoid
    /// handing out what it was about to.
    /// <para>
    /// This is the one place a furniture row is marked deleted, so it is also the one place
    /// <see cref="ItemDeletedEvent" /> is published. <paramref name="reason" /> is what tells the
    /// forensics record a cracked egg from a binned sticky, since the row itself no longer can.
    /// </para>
    /// </summary>
    internal async Task<bool> ConsumeItemAsync(
        ActionContext ctx,
        IRoomItem item,
        ItemDeletionReason reason,
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            // Loaded rather than attached blind, and the query is the guard: a row already marked
            // deleted does not come back, so a second consumption of the same item writes nothing
            // and reports false instead of stamping DeletedAt again and handing out a second prize.
            // The guard that used to stand for this -- the item being taken out of ItemsById --
            // lands twenty lines and two awaits after the write (RSYS-CONSUME-049).
            FurnitureEntity? entity = await dbCtx
                .Furnitures.FirstOrDefaultAsync(
                    f => f.Id == item.ObjectId.Value && f.DeletedAt == null,
                    ct
                )
                .ConfigureAwait(true);

            if (entity is null)
            {
                _logger.LogDebug(
                    "Consumption of furniture {ObjectId} in room {RoomId} found nothing to delete.",
                    item.ObjectId.Value,
                    _state.RoomId.Value
                );

                return false;
            }

            entity.DeletedAt = DateTime.UtcNow;

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete consumed furniture {ObjectId} in room {RoomId}",
                item.ObjectId.Value,
                _state.RoomId.Value
            );

            return false;
        }

        await ObjectModule.RemoveObjectAsync(ctx, item, ct, item.OwnerId).ConfigureAwait(true);

        await _events
            .PublishAsync(
                new ItemDeletedEvent(
                    item.ObjectId.Value,
                    item.OwnerId.Value,
                    ctx.PlayerId == PlayerId.Invalid ? null : ctx.PlayerId.Value,
                    reason
                ),
                ct
            )
            .ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// Runs the payout half of a consumed-furniture prize under a journal operation, so a prize that
    /// was paid for with a furniture and never arrived is something an operator can find.
    /// </summary>
    /// <remarks>
    /// The caller has destroyed the furniture before calling this, on purpose — the reverse order
    /// lets a repeated click mint prizes from one item — so the prize is owed from before the payout
    /// runs. That is the whole of <c>RecordOwedPayoutAsync</c>, which a quest reward and an
    /// achievement level reach by their own route (RSYS-PRIZE-050, PROG-REWARD-032).
    /// </remarks>
    internal Task GrantConsumedLootAsync(
        PlayerId beneficiary,
        string detail,
        Func<CancellationToken, Task> grantAsync,
        CancellationToken ct
    ) =>
        _commerceJournal.RecordOwedPayoutAsync(
            CommerceOperationKind.LootGrant,
            (int)beneficiary,
            detail,
            grantAsync,
            _logger,
            ct
        );

    /// <summary>
    /// The wall furniture a bin button may destroy. Stickies and photos are consumable by design —
    /// nobody expects a note back — and <c>postit</c> is the same furniture under the name the
    /// Arcturus dump left on two definitions, which no side registers as a logic.
    /// </summary>
    private static readonly string[] DisposableWallLogics =
    [
        "furniture_stickie",
        "furniture_external_image_wallitem",
        "postit",
    ];

    public async Task<bool> DeleteDisposableWallItemAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        // Rights rather than ownership, like writing on the note in the first place: a room owner
        // expects to be able to clear a sticky a visitor left on their wall. What keeps that safe is
        // the family check below, not the actor check.
        if (item is not IRoomWallItem)
        {
            return false;
        }

        if (Array.IndexOf(DisposableWallLogics, item.Definition.LogicName) < 0)
        {
            _logger.LogWarning(
                "Player {PlayerId} tried to bin {ObjectId} (definition {DefinitionId}, logic '{Logic}') in room {RoomId}; only stickies and photos are disposable.",
                ctx.PlayerId.Value,
                itemId.Value,
                item.Definition.Id,
                item.Definition.LogicName,
                _state.RoomId.Value
            );

            return false;
        }

        return await ConsumeItemAsync(ctx, item, ItemDeletionReason.Binned, ct)
            .ConfigureAwait(true);
    }

    public async Task<bool> EnterOneWayDoorAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        if (
            !_state.ItemsById.TryGetValue(itemId, out IRoomItem? item)
            || item is not IRoomFloorItem gate
            || gate.Logic is not FurnitureOneWayDoorLogic
            || !_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId avatarObjectId)
            || !_state.AvatarsByObjectId.TryGetValue(avatarObjectId, out IRoomAvatar? avatar)
        )
        {
            return false;
        }

        // All four definitions are can_walk 0, so nobody can be standing on the gate itself: the
        // player waits on the tile behind it and comes out in front. "Behind" and "in front" are the
        // gate's own rotation, which is why turning one round reverses which way it lets people
        // through — the whole point of a one-way gate.
        (int dx, int dy) = gate.Rotation.ToDelta();

        if (avatar.X != gate.X - dx || avatar.Y != gate.Y - dy)
        {
            return false;
        }

        int exitIdx = MapModule.ToIdx(gate.X + dx, gate.Y + dy);

        if (!MapModule.InBounds(exitIdx))
        {
            return false;
        }

        RoomTileFlags exitFlags = _state.TileFlags[exitIdx];

        if (
            !exitFlags.Has(RoomTileFlags.Walkable)
            || exitFlags.Has(RoomTileFlags.Disabled)
            || exitFlags.Has(RoomTileFlags.AvatarOccupied)
        )
        {
            return false;
        }

        // Open, move, close. The status is pushed rather than written through SetStateAsync on
        // purpose: a gate is open only for the instant somebody is inside it, and persisting that
        // would leave one stuck open across a reload if the server stopped mid-pass.
        await SendComposerToRoomAsync(
                new OneWayDoorStatusMessageComposer
                {
                    FurniId = itemId.Value,
                    Status = FurnitureOneWayDoorLogic.StatusOpen,
                }
            )
            .ConfigureAwait(true);

        // Same reason as every other relocation: a queued path routed from the tile in front of the
        // door walks the avatar back out of it.
        AvatarModule.CancelWalk(avatar);

        MapModule.RollAvatar(avatar, exitIdx, _state.TileHeights[exitIdx]);

        Rotation facing = gate.Rotation;

        avatar.SetBodyRotation(facing);
        avatar.SetHeadRotation(facing);

        await SendComposerToRoomAsync(
                new UserUpdateMessageComposer { Avatars = [avatar.GetSnapshot()] }
            )
            .ConfigureAwait(true);

        await SendComposerToRoomAsync(
                new OneWayDoorStatusMessageComposer
                {
                    FurniId = itemId.Value,
                    Status = FurnitureOneWayDoorLogic.StatusClosed,
                }
            )
            .ConfigureAwait(true);

        return true;
    }

    public async Task<bool> SetBackgroundColorAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int hue,
        int saturation,
        int lightness,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        if (item?.Logic is not FurnitureBackgroundColorLogic toner)
        {
            return false;
        }

        await toner.SetColorAsync(hue, saturation, lightness).ConfigureAwait(true);

        return true;
    }

    public async Task<CreditFurniPayout> RedeemCreditFurniAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        // Ownership, like the present: a credit furni is money, and room rights would let whoever
        // holds them cash in every coin a visitor put down.
        if (
            !_state.ItemsById.TryGetValue(itemId, out IRoomItem? item)
            || item.OwnerId != ctx.PlayerId
        )
        {
            return CreditFurniPayout.None;
        }

        CurrencyType currency;
        int amount;

        if (CreditFurniValue.TryParse(item.Definition.Name, out int credits))
        {
            currency = CurrencyType.Credits;
            amount = credits;
        }
        else if (EmeraldFurniValue.TryParse(item.Definition.Name, out int emeralds))
        {
            // The collectibles coin. Its own convention, read the way the client's
            // FurnitureNftCreditLogic reads it, so the prompt's "contains N emeralds" and the payout
            // are the same number.
            currency = CurrencyType.Emeralds;
            amount = emeralds;
        }
        else
        {
            _logger.LogWarning(
                "Credit furni {ObjectId} (definition {DefinitionId}, '{Name}') in room {RoomId} names no value; it cannot be redeemed.",
                itemId.Value,
                item.Definition.Id,
                item.Definition.Name,
                _state.RoomId.Value
            );

            return CreditFurniPayout.None;
        }

        // Consumed before the credits exist, for the reason the crackable is: the reverse order
        // turns one coin still standing on the floor into as many payouts as it can be clicked.
        return await ConsumeItemAsync(ctx, item, ItemDeletionReason.CreditRedeemed, ct)
            .ConfigureAwait(true)
            ? new CreditFurniPayout { Amount = amount, Currency = currency }
            : CreditFurniPayout.None;
    }

    public async Task<PresentContentsSnapshot?> OpenPresentAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        // Ownership, not room rights, and the one place in this file that differs: a mannequin is
        // furniture the room shares, a present is somebody's post. Room rights would let anyone with
        // build rights open every gift dropped in their room.
        if (
            !_state.ItemsById.TryGetValue(itemId, out IRoomItem? item)
            || item.OwnerId != ctx.PlayerId
            || item.Logic is not FurniturePresentLogic present
        )
        {
            return null;
        }

        PresentContentsSnapshot? contents = present.ReadContents();

        if (contents is null)
        {
            // An empty wrapping is left standing rather than eaten. It is either a present from
            // before gifts were wrapped or one placed by the furni editor, and consuming it would
            // destroy furniture to hand back nothing.
            _logger.LogWarning(
                "Present {ObjectId} (definition {DefinitionId}) in room {RoomId} carries no contents section; it cannot be opened.",
                itemId.Value,
                item.Definition.Id,
                _state.RoomId.Value
            );

            return null;
        }

        if (
            !await ConsumeItemAsync(ctx, item, ItemDeletionReason.PresentOpened, ct)
                .ConfigureAwait(true)
        )
        {
            return null;
        }

        await _events
            .PublishAsync(
                new PresentOpenedEvent(
                    itemId.Value,
                    ctx.PlayerId.Value,
                    _state.RoomId.Value,
                    contents.OfferId
                ),
                ct
            )
            .ConfigureAwait(true);

        return contents;
    }

    public async Task<RoomDimmerStateSnapshot?> GetDimmerStateAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        FurnitureRoomDimmerLogic? dimmer = await FindDimmerAsync(ctx, itemId).ConfigureAwait(true);

        return dimmer is null ? null : Describe(itemId, dimmer);
    }

    public async Task<RoomDimmerStateSnapshot?> ToggleDimmerAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        FurnitureRoomDimmerLogic? dimmer = await FindDimmerAsync(ctx, itemId).ConfigureAwait(true);

        if (dimmer is null)
        {
            return null;
        }

        await dimmer.TogglePowerAsync().ConfigureAwait(true);

        return Describe(itemId, dimmer);
    }

    public async Task<RoomDimmerStateSnapshot?> SaveDimmerPresetAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int presetNumber,
        int effectId,
        string colorHex,
        int brightness,
        bool apply,
        CancellationToken ct
    )
    {
        FurnitureRoomDimmerLogic? dimmer = await FindDimmerAsync(ctx, itemId).ConfigureAwait(true);

        if (dimmer is null)
        {
            return null;
        }

        await dimmer
            .SavePresetAsync(presetNumber, effectId, colorHex, brightness, apply)
            .ConfigureAwait(true);

        return Describe(itemId, dimmer);
    }

    private async Task<FurnitureRoomDimmerLogic?> FindDimmerAsync(
        ActionContext ctx,
        RoomObjectId itemId
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        return item?.Logic as FurnitureRoomDimmerLogic;
    }

    private static RoomDimmerStateSnapshot Describe(
        RoomObjectId itemId,
        FurnitureRoomDimmerLogic dimmer
    ) =>
        new()
        {
            ItemId = itemId,
            Presets = dimmer.GetPresets(),
            SelectedPresetId = dimmer.SelectedPresetId,
            IsOn = dimmer.IsOn,
        };

    public async Task<string?> GetItemDataAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        // The legacy projection rather than the typed snapshot: it is what the wall item already
        // puts on the wire, so the answer and the room's own copy cannot disagree.
        return item?.Logic.StuffData.GetLegacyString();
    }

    public async Task<bool> SetClothingChangeDataAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        string gender,
        string look,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        if (item?.Logic.StuffData is not ILegacyStuffData legacy)
        {
            return false;
        }

        // Merge, never overwrite: one message carries one gender and the booth holds both.
        legacy.SetState(ClothingChangeData.Merge(legacy.GetLegacyString(), gender, look));

        await item.Logic.PersistStuffDataAsync().ConfigureAwait(true);

        return true;
    }

    public async Task<bool> SetObjectDataAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        IReadOnlyList<(string Key, string Value)> pairs,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        if (item?.Logic.StuffData is not IMapStuffData map)
        {
            return false;
        }

        foreach ((string key, string value) in pairs)
        {
            // An empty key would be a field with no name: it survives a round trip through the map
            // and comes back as a blank row in every editor that reads the furni afterwards.
            if (!string.IsNullOrEmpty(key))
            {
                map.Data[key] = value;
            }
        }

        await item.Logic.PersistStuffDataAsync().ConfigureAwait(true);

        return true;
    }

    public async Task<bool> SetPostItAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        string colorHex,
        string text,
        CancellationToken ct
    )
    {
        IRoomItem? item = await FindManipulableItemAsync(ctx, itemId).ConfigureAwait(true);

        if (item?.Logic.StuffData is not ILegacyStuffData legacy)
        {
            return false;
        }

        // One legacy string carries both, colour first: the client splits on the first space and
        // treats everything after it as the note body, newlines included.
        legacy.SetState($"{colorHex} {text}");

        await item.Logic.PersistStuffDataAsync().ConfigureAwait(true);

        return true;
    }
}
