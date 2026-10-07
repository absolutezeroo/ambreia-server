using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>Pets placed in the room: placement, care, commands and riding.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomPets")]
public interface IRoomPets : IGrainWithIntegerKey
{
    public Task<PetSnapshot?> PlacePetAsync(
        ActionContext ctx,
        int petId,
        int x,
        int y,
        Rotation direction,
        CancellationToken ct
    );

    public Task<PetSnapshot?> MovePetAsync(
        ActionContext ctx,
        int petId,
        int x,
        int y,
        Rotation direction,
        CancellationToken ct
    );

    public Task<PetSnapshot?> PickUpPetAsync(ActionContext ctx, int petId, CancellationToken ct);
    public Task<PetSnapshot?> GetPlacedPetSnapshotAsync(int petId, CancellationToken ct);
    public Task<ImmutableArray<PetSnapshot>> GetPlacedPetSnapshotsAsync(CancellationToken ct);

    /// <summary>Gets a player on or off a pet; the client sends one message for both.</summary>
    public Task MountPetAsync(ActionContext ctx, int petId, bool mount, CancellationToken ct);

    /// <summary>
    /// Fits a saddle: the owner uses a <c>PetSaddle</c> furniture on their pet (header 2099,
    /// <c>CustomizePetWithFurni</c>). The furniture is spent.
    /// </summary>
    public Task ApplySaddleToPetAsync(
        ActionContext ctx,
        int petId,
        RoomObjectId saddleItemId,
        CancellationToken ct
    );

    public Task RemoveSaddleFromPetAsync(ActionContext ctx, int petId, CancellationToken ct);

    public Task TogglePetRidingPermissionAsync(ActionContext ctx, int petId, CancellationToken ct);
}
