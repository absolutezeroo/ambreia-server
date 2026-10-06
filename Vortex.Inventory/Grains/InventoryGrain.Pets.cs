using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Pets;
using Vortex.Primitives.Events;
using Vortex.Primitives.Pets;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Inventory.Grains;

public sealed partial class InventoryGrain
{
    /// <summary>
    /// The row a pet grant produces, without committing it. A catalog offer can carry furniture, a
    /// badge, a pet and a bot at once, and every one of those used to be its own commit — so a
    /// failure partway through left the earlier families delivered and the whole purchase refunded.
    /// Building the entity separately from committing it is what lets them all share one
    /// transaction.
    /// </summary>
    internal PetEntity BuildPetEntity(PetCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Pet name is required.", nameof(request));
        }

        return new PetEntity
        {
            OwnerPlayerEntityId = (int)this.GetPrimaryKeyLong(),
            RoomEntityId = null,
            Name = request.Name.Trim(),
            Type = request.Type,
            Race = request.Race,
            Color = request.Color,
            Gender = request.Gender,
            Level = 1,
            Experience = 0,
            Respect = 0,
            X = 0,
            Y = 0,
            Z = 0,
            Direction = (int)Rotation.South,
            OwnerPlayerEntity = null!,
        };
    }

    public async Task<PetSnapshot> CreatePetAsync(PetCreateRequest request, CancellationToken ct)
    {
        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            PetEntity entity = BuildPetEntity(request);

            dbCtx.Pets.Add(entity);
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            await _events
                .PublishAsync(
                    new PetAdoptedEvent(
                        (int)this.GetPrimaryKeyLong(),
                        entity.Id,
                        entity.Name,
                        entity.Type
                    ),
                    ct
                )
                .ConfigureAwait(true);

            return ToSnapshot(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create pet for player {PlayerId}",
                this.GetPrimaryKeyLong()
            );
            throw;
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }
    }

    public async Task<PetSnapshot?> GetPetSnapshotAsync(int petId, CancellationToken ct)
    {
        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            PetEntity? entity = await dbCtx
                .Pets.AsNoTracking()
                .Where(p =>
                    p.Id == petId
                    && p.OwnerPlayerEntityId == (int)this.GetPrimaryKeyLong()
                    && p.DeletedAt == null
                )
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(true);

            return entity is null ? null : ToSnapshot(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get pet {PetId} for player {PlayerId}",
                petId,
                this.GetPrimaryKeyLong()
            );
            throw;
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }
    }

    public async Task<ImmutableArray<PetSnapshot>> GetAllPetSnapshotsAsync(CancellationToken ct)
    {
        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            PetEntity[] entities = await dbCtx
                .Pets.AsNoTracking()
                .Where(p =>
                    p.OwnerPlayerEntityId == (int)this.GetPrimaryKeyLong()
                    && p.RoomEntityId == null
                    && p.DeletedAt == null
                )
                .OrderBy(p => p.Id)
                .ToArrayAsync(ct)
                .ConfigureAwait(true);

            return entities.Select(ToSnapshot).ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to list pet inventory for player {PlayerId}",
                this.GetPrimaryKeyLong()
            );
            throw;
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }
    }

    internal static PetSnapshot ToSnapshot(PetEntity entity)
    {
        return new PetSnapshot
        {
            PetId = entity.Id,
            OwnerId = new PlayerId(entity.OwnerPlayerEntityId),
            RoomId = entity.RoomEntityId,
            Name = entity.Name,
            Type = entity.Type,
            Race = entity.Race,
            Color = entity.Color,
            Gender = entity.Gender,
            Level = entity.Level,
            Experience = entity.Experience,
            Energy = entity.Energy,
            Nutrition = entity.Nutrition,
            Respect = entity.Respect,
            X = entity.X,
            Y = entity.Y,
            Z = entity.Z,
            Direction = (Rotation)entity.Direction,
        };
    }
}
