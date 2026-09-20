using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Rooms.Grains.Modules;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// Feeding a pet what you are holding. Kept apart from the bowl-feeding path because it is a
/// different thing on both sides: the food is a hand item rather than a piece of furniture, it has
/// no uses to count down, and it comes from a hand that has to be emptied.
/// </summary>
public sealed partial class RoomPetSystem
{
    /// <summary>
    /// Gives a pet whatever the actor is holding. False when the actor is empty-handed, when the
    /// pet is out of reach, or when the item is nothing a pet will take — a camera, a bunch of
    /// roses — in which case the actor keeps holding it.
    /// </summary>
    public async Task<bool> ConsumeHandItemAsync(ActionContext ctx, int petId, CancellationToken ct)
    {
        await EnsurePetsLoadedAsync(ct);

        int handItemId = _roomGrain.HandItemModule.HeldBy(ctx.PlayerId);

        if (handItemId == 0 || !_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet))
        {
            return false;
        }

        if (!IsActorWithinReachOfPet(ctx, pet))
        {
            return false;
        }

        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        HandItemEntity? handItem = await dbCtx
            .HandItems.AsNoTracking()
            .SingleOrDefaultAsync(h => h.HandItemId == handItemId && h.DeletedAt == null, ct);

        if (handItem is null || (handItem.Nutrition <= 0 && handItem.Thirst <= 0))
        {
            // Nothing a pet wants. Leaving it in the hand is kinder than swallowing it silently.
            return false;
        }

        int nutritionCap = _roomGrain._petLevelProvider.GetNutritionCapForLevel(
            pet.Type,
            pet.Level
        );

        PetSnapshot fed = pet with
        {
            Nutrition = Math.Min(pet.Nutrition + handItem.Nutrition, nutritionCap),
            Thirst = Math.Min(pet.Thirst + handItem.Thirst, RoomPetRuntime.ThirstCap),
        };

        _roomGrain._state.PetsById[petId] = fed;

        if (_motionByPetId.TryGetValue(petId, out PetMotionState? motion))
        {
            // The stats have to reach the database eventually, and this rides the same dirty-set
            // the bowl path does rather than writing through.
            motion.IsStatsDirty = true;
        }

        // The hand empties whether or not the pet needed it: it has been eaten either way.
        _ = _roomGrain.HandItemModule.Drop(ctx.PlayerId);

        await SendPetUpdatedAsync(fed, ct);
        await BroadcastPetVocalAsync(fed, "GENERIC_HAPPY");

        // The nutrition the item was worth, not the one the pet could still take: a full pet is
        // still being fed, and "giving pets %limit% points of food" counts what was given.
        if (handItem.Nutrition > 0)
        {
            _roomGrain.PublishDetached(
                new PetFedEvent(ctx.PlayerId, fed.OwnerId, petId, fed.Type, handItem.Nutrition)
            );
        }

        return true;
    }

    /// <summary>
    /// Reach is one tile, the same rule that governs passing a hand item to a person — and the same
    /// rule the client follows when it decides whether to offer the button.
    /// </summary>
    private bool IsActorWithinReachOfPet(ActionContext ctx, PetSnapshot pet) =>
        _roomGrain._state.AvatarsByPlayerId.TryGetValue(
            ctx.PlayerId,
            out Primitives.Rooms.Object.RoomObjectId objectId
        )
        && _roomGrain._state.AvatarsByObjectId.TryGetValue(
            objectId,
            out Primitives.Rooms.Object.Avatars.IRoomAvatar? avatar
        )
        && RoomHandItemModule.IsWithinReach(avatar.X, avatar.Y, pet.X, pet.Y);
}
