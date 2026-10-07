using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Action;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public async Task<PetSnapshot?> PlacePetAsync(
        ActionContext ctx,
        int petId,
        int x,
        int y,
        Rotation direction,
        CancellationToken ct
    )
    {
        try
        {
            return await PetSystem.PlacePetAsync(ctx, petId, x, y, direction, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to place pet {PetId} in room {RoomId} at ({X}, {Y})",
                petId,
                _state.RoomId,
                x,
                y
            );

            return null;
        }
    }

    public async Task<PetSnapshot?> MovePetAsync(
        ActionContext ctx,
        int petId,
        int x,
        int y,
        Rotation direction,
        CancellationToken ct
    )
    {
        try
        {
            return await PetSystem.MovePetAsync(ctx, petId, x, y, direction, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to move pet {PetId} in room {RoomId} to ({X}, {Y})",
                petId,
                _state.RoomId,
                x,
                y
            );

            return null;
        }
    }

    public async Task<PetSnapshot?> PickUpPetAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        try
        {
            return await PetSystem.PickUpPetAsync(ctx, petId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to pick up pet {PetId} in room {RoomId}",
                petId,
                _state.RoomId
            );

            return null;
        }
    }

    public Task<PetSnapshot?> GetPlacedPetSnapshotAsync(int petId, CancellationToken ct) =>
        PetSystem.GetPlacedPetSnapshotAsync(petId, ct);

    public Task<ImmutableArray<PetSnapshot>> GetPlacedPetSnapshotsAsync(CancellationToken ct) =>
        PetSystem.GetPlacedPetSnapshotsAsync(ct);

    public async Task MountPetAsync(ActionContext ctx, int petId, bool mount, CancellationToken ct)
    {
        try
        {
            await PetSystem.MountPetAsync(ctx, petId, mount, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to {Action} pet {PetId} in room {RoomId}",
                mount ? "mount" : "dismount",
                petId,
                _state.RoomId
            );
        }
    }

    public async Task ApplySaddleToPetAsync(
        ActionContext ctx,
        int petId,
        RoomObjectId saddleItemId,
        CancellationToken ct
    )
    {
        try
        {
            await PetSystem.ApplySaddleToPetAsync(ctx, petId, saddleItemId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fit saddle {ItemId} on pet {PetId} in room {RoomId}",
                saddleItemId,
                petId,
                _state.RoomId
            );
        }
    }

    public async Task RemoveSaddleFromPetAsync(ActionContext ctx, int petId, CancellationToken ct)
    {
        try
        {
            await PetSystem.RemoveSaddleFromPetAsync(ctx, petId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to remove the saddle from pet {PetId} in room {RoomId}",
                petId,
                _state.RoomId
            );
        }
    }

    public async Task TogglePetRidingPermissionAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        try
        {
            await PetSystem.TogglePetRidingPermissionAsync(ctx, petId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to toggle riding permission for pet {PetId} in room {RoomId}",
                petId,
                _state.RoomId
            );
        }
    }
}
