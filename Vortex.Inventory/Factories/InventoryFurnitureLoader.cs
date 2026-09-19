using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Furniture;
using Vortex.Inventory.Furniture;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Inventory.Factories;
using Vortex.Primitives.Inventory.Furniture;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Snapshots.Furniture;

namespace Vortex.Inventory.Factories;

internal sealed class InventoryFurnitureLoader(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IFurnitureDefinitionProvider defsProvider,
    IStuffDataFactory stuffDataFactory,
    IGrainFactory grainFactory,
    ILogger<InventoryFurnitureLoader> logger
) : IInventoryFurnitureLoader
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IFurnitureDefinitionProvider _defsProvider = defsProvider;
    private readonly IStuffDataFactory _stuffDataFactory = stuffDataFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<InventoryFurnitureLoader> _logger = logger;

    public async Task<IReadOnlyList<IFurnitureItem>> LoadByPlayerIdAsync(
        PlayerId playerId,
        CancellationToken ct
    )
    {
        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        try
        {
            List<FurnitureEntity> entities = await dbCtx
                .Furnitures.AsNoTracking()
                .Where(x =>
                    x.PlayerEntityId == (int)playerId
                    && x.RoomEntityId == null
                    // An item sitting in a wired chest is out of its owner's hands until it is
                    // withdrawn. Without this it would be listed here as well, and the same row
                    // would exist twice on screen: once in the inventory, once in the chest.
                    && x.WiredChestEntityId == null
                    // And the same for a song disk loaded into a jukebox.
                    && x.JukeboxEntityId == null
                    && x.DeletedAt == null
                )
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<IFurnitureItem> items = new List<IFurnitureItem>();

            string ownerName = await _grainFactory
                .GetPlayerDirectoryGrain()
                .GetPlayerNameAsync(playerId, ct)
                .ConfigureAwait(false);

            foreach (FurnitureEntity entity in entities)
            {
                try
                {
                    IFurnitureItem item = CreateFromEntity(entity, ownerName);

                    items.Add(item);
                }
                catch (Exception ex)
                {
                    // The definition id is the whole diagnosis when this is the usual cause -- a
                    // furniture row pointing at a definition the catalogue no longer has -- and
                    // without it the warning names an item nobody can look up.
                    _logger.LogWarning(
                        ex,
                        "Failed to load inventory furniture {ItemId} (definition {DefinitionId}) for player {PlayerId}; skipping item.",
                        entity.Id,
                        entity.FurnitureDefinitionEntityId,
                        playerId
                    );
                }
            }

            return items;
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(false);
        }
    }

    public IFurnitureItem CreateFromEntity(FurnitureEntity entity, string? ownerName)
    {
        FurnitureDefinitionSnapshot definition =
            _defsProvider.TryGetDefinition(entity.FurnitureDefinitionEntityId)
            ?? throw new VortexException(VortexErrorCodeEnum.FurnitureDefinitionNotFound);

        ExtraData extraData = new ExtraData(entity.ExtraData);

        return new FurnitureItem()
        {
            ItemId = entity.Id,
            OwnerId = entity.PlayerEntityId,
            OwnerName = ownerName ?? string.Empty,
            Definition = definition,
            ExtraData = extraData,
            // The whole blob, not the extracted "stuff" section: the factory looks that section up
            // itself, so handing it the section's own body made every lookup miss and every loaded
            // item come back with default stuff data.
            StuffData = _stuffDataFactory.CreateStuffDataFromExtraData(
                definition.StuffDataType,
                extraData
            ),
        };
    }

    public IFurnitureItem CreateFromRoomItemSnapshot(RoomItemSnapshot snapshot)
    {
        FurnitureDefinitionSnapshot definition =
            _defsProvider.TryGetDefinition(snapshot.DefinitionId)
            ?? throw new VortexException(VortexErrorCodeEnum.FurnitureDefinitionNotFound);

        return new FurnitureItem()
        {
            ItemId = snapshot.ObjectId,
            OwnerId = snapshot.OwnerId,
            OwnerName = snapshot.OwnerName,
            Definition = definition,
            ExtraData = new ExtraData(snapshot.ExtraData),
            StuffData = _stuffDataFactory.CreateStuffDataFromJson(
                (StuffDataType)snapshot.StuffData.StuffBitmask,
                snapshot.ExtraData
            ),
        };
    }
}
