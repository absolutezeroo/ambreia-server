using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Pets;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// Saddles, riding and the status push that tells the client which pet actions to offer.
/// </summary>
public sealed partial class RoomPetSystem
{
    /// <summary>Gets on or off a pet. The client sends one message for both.</summary>
    public async Task MountPetAsync(ActionContext ctx, int petId, bool mount, CancellationToken ct)
    {
        await EnsurePetsLoadedAsync(ct);

        if (!_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet))
        {
            return;
        }

        if (mount)
        {
            // A pet with no saddle cannot be ridden, someone else's pet needs their permission, and
            // a pet already carrying a rider is taken.
            bool allowed =
                pet.HasSaddle
                && pet.RiderId is null
                && (pet.OwnerId == ctx.PlayerId || pet.RidingPermission);

            if (!allowed)
            {
                return;
            }

            await ApplyRidingStateAsync(pet, pet.HasSaddle, ctx.PlayerId, ct);

            return;
        }

        // Only the rider gets off, and only from the pet they are on.
        if (pet.RiderId != ctx.PlayerId)
        {
            return;
        }

        await ApplyRidingStateAsync(pet, pet.HasSaddle, rider: null, ct);
    }

    /// <summary>
    /// Fits a saddle (header 2099, <c>CustomizePetWithFurni</c>). The client offers the furniture
    /// from <c>FurniturePetCustomizationLogic</c> and recognises a saddle by its furniture category,
    /// <c>PET_SADDLE</c>, and only for the furniture owner's own pets -- the same three rules are
    /// checked here, because the packet carries nothing but the two ids. A pet already saddled is
    /// refused rather than handed a second one it would not show.
    /// </summary>
    public async Task ApplySaddleToPetAsync(
        ActionContext ctx,
        int petId,
        RoomObjectId saddleItemId,
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        if (
            !_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet)
            || pet.OwnerId != ctx.PlayerId
            || pet.HasSaddle
            || !_roomGrain._state.ItemsById.TryGetValue(saddleItemId, out IRoomItem? saddle)
            || saddle.Definition.FurniCategory != FurnitureCategory.PetSaddle
            || saddle.OwnerId != ctx.PlayerId
        )
        {
            return;
        }

        // Spent first: the other order lets a repeated click saddle for free.
        if (!await _roomGrain.ConsumeItemAsync(ctx, saddle, ItemDeletionReason.PetSaddleFitted, ct))
        {
            return;
        }

        // Re-read: the pet kept ticking while the furniture row was written.
        if (_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? current))
        {
            await ApplyRidingStateAsync(current, hasSaddle: true, current.RiderId, ct);
        }
    }

    /// <summary>Takes the saddle off, which also throws whoever is riding.</summary>
    public async Task RemoveSaddleFromPetAsync(ActionContext ctx, int petId, CancellationToken ct)
    {
        await EnsurePetsLoadedAsync(ct);

        if (
            !_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet)
            || pet.OwnerId != ctx.PlayerId
        )
        {
            return;
        }

        await ApplyRidingStateAsync(pet, hasSaddle: false, rider: null, ct);
    }

    /// <summary>Flips whether players other than the owner may ride.</summary>
    public async Task TogglePetRidingPermissionAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        if (
            !_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet)
            || pet.OwnerId != ctx.PlayerId
        )
        {
            return;
        }

        PetSnapshot updated = pet with { RidingPermission = !pet.RidingPermission };
        _roomGrain._state.PetsById[petId] = updated;

        await PersistRidingAsync(updated, ct);
        await SendPetStatusAsync(updated);
    }

    /// <summary>
    /// Tells the room which actions a pet currently offers. Sent whenever the answer can have
    /// changed -- the client has no other way to learn it. Breeding and monsterplants were removed
    /// in M0, so the four flags the message still carries are always false.
    /// </summary>
    internal Task SendPetStatusAsync(PetSnapshot pet) =>
        _roomGrain.SendComposerToRoomAsync(
            new PetStatusUpdateMessageComposer
            {
                RoomIndex = RoomPetRuntime.ToRoomObjectId(pet.PetId).Value,
                PetId = pet.PetId,
                CanBreed = false,
                CanHarvest = false,
                CanRevive = false,
                HasBreedingPermission = false,
            }
        );

    /// <summary>Pushes a pet's saddle and rider so the room redraws it.</summary>
    private Task SendPetFigureAsync(PetSnapshot pet) =>
        _roomGrain.SendComposerToRoomAsync(
            new PetFigureUpdateMessageComposer
            {
                RoomIndex = RoomPetRuntime.ToRoomObjectId(pet.PetId).Value,
                PetId = pet.PetId,
                Pet = pet,
                HasSaddle = pet.HasSaddle,
                IsRiding = pet.RiderId is not null,
            }
        );

    private async Task ApplyRidingStateAsync(
        PetSnapshot pet,
        bool hasSaddle,
        PlayerId? rider,
        CancellationToken ct
    )
    {
        PetSnapshot updated = pet with { HasSaddle = hasSaddle, RiderId = rider };
        _roomGrain._state.PetsById[pet.PetId] = updated;

        await PersistRidingAsync(updated, ct);
        await SendPetFigureAsync(updated);
        await SendPetStatusAsync(updated);
    }

    /// <summary>The saddle and the permission survive a restart; the rider does not.</summary>
    private async Task PersistRidingAsync(PetSnapshot pet, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        PetEntity? entity = await LoadPlacedPetAsync(dbCtx, pet.PetId, ct);

        if (entity is null)
        {
            return;
        }

        entity.HasSaddle = pet.HasSaddle;
        entity.RidingPermission = pet.RidingPermission;

        await dbCtx.SaveChangesAsync(ct);
    }
}
