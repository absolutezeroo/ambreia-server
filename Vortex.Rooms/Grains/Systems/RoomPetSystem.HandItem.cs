using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Action;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Rooms.Grains.Modules;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// Giving a pet what you are holding. Pets have no needs since M0, so the treat changes no stat:
/// the hand empties and the pet looks pleased.
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

        // The hand_items row still says which items a pet takes at all: one with neither food nor
        // water value is nothing a pet wants, and stays in the hand rather than being swallowed
        // silently.
        if (handItem is null || (handItem.Nutrition <= 0 && handItem.Thirst <= 0))
        {
            return false;
        }

        _ = _roomGrain.HandItemModule.Drop(ctx.PlayerId);

        await BroadcastPetVocalAsync(pet, "GENERIC_HAPPY");

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
