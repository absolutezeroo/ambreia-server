using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Commerce;
using Vortex.Database.Context;
using Vortex.Database.Entities.Commerce;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Pets;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Furniture;
using Vortex.Inventory.Fulfillment;
using Vortex.Inventory.Furniture;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.Snapshots.StuffData;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Groups;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Inventory.Furniture;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Pets;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Primitives.Sound;
using Vortex.Primitives.Sound.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Inventory.Bots;

namespace Vortex.Inventory.Grains;

public sealed partial class InventoryGrain
{
    /// <summary>
    /// Rebuilds the cached furniture list from the database, then tells the client to ask for it
    /// again.
    /// </summary>
    /// <remarks>
    /// The notification is the point. This exists for the wired chest settlements, which move rows
    /// by writing <c>WiredChestEntityId</c> straight through EF rather than going through
    /// <see cref="AddFurnitureAsync"/> -- so nothing on either side ever reached the client, and a
    /// deposited item sat in the player's inventory looking like it had never left. Reloading only
    /// fixed the grain's own copy, which is the half nobody can see.
    /// </remarks>
    public async Task ReloadFurnitureAsync(CancellationToken ct)
    {
        await _furniModule.ReloadAsync(ct);

        await _grainFactory
            .GetPlayerPresenceGrain(this.GetPrimaryKeyLong())
            .OnFurnitureListInvalidatedAsync(ct);
    }

    public async Task<bool> AddFurnitureAsync(IFurnitureItem item, CancellationToken ct)
    {
        if (!await _furniModule.AddFurnitureAsync(item, ct))
        {
            return false;
        }

        IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(
            this.GetPrimaryKeyLong()
        );

        await presence.OnFurnitureAddedAsync(item.GetSnapshot(), ct);
        await RefreshMysteryBoxTrackerAsync(item.Definition.Id, ct).ConfigureAwait(true);

        return true;
    }

    public async Task<bool> AddFurnitureFromRoomItemSnapshotAsync(
        RoomItemSnapshot snapshot,
        CancellationToken ct
    )
    {
        IFurnitureItem item = _furnitureItemsLoader.CreateFromRoomItemSnapshot(snapshot);

        if (!await _furniModule.AddFurnitureAsync(item, ct))
        {
            return false;
        }

        IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(
            this.GetPrimaryKeyLong()
        );

        await presence.OnFurnitureAddedAsync(item.GetSnapshot(), ct);

        return true;
    }

    public async Task<bool> RemoveFurnitureAsync(RoomObjectId itemId, CancellationToken ct)
    {
        // Read the definition before the item is gone: the mystery box tracker is derived from the
        // boxes a player owns, so losing one has to refresh it and afterwards there is nothing left
        // to look the definition up from.
        FurnitureItemSnapshot? leaving = await _furniModule
            .GetItemSnapshotAsync(itemId, ct)
            .ConfigureAwait(true);

        if (!await _furniModule.RemoveFurnitureAsync(itemId, ct))
        {
            return false;
        }

        IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(
            this.GetPrimaryKeyLong()
        );

        await presence.OnFurnitureRemovedAsync(itemId, ct);

        if (leaving is not null)
        {
            await RefreshMysteryBoxTrackerAsync(leaving.Definition.Id, ct).ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>
    /// Re-pushes the mystery box toolbar tracker when the furniture that just entered or left the
    /// inventory is a registered box. The tracker shows the colour of a box the player owns, so a
    /// catalogue purchase, a trade or a staff grant all change it — without this it would only
    /// correct itself on the next login. Gated on the definition actually being a box, so every
    /// other furniture movement costs one cached dictionary lookup and nothing else.
    /// </summary>
    private async Task RefreshMysteryBoxTrackerAsync(int definitionId, CancellationToken ct)
    {
        bool isBox = await _grainFactory
            .GetMysteryBoxManagerGrain()
            .IsBoxDefinitionAsync(definitionId, ct)
            .ConfigureAwait(true);

        if (!isBox)
        {
            return;
        }

        await _grainFactory
            .GetPlayerMysteryBoxGrain(this.GetPrimaryKeyLong())
            .PushTrackerAsync(ct)
            .ConfigureAwait(true);
    }

    public async Task GrantCatalogOfferAsync(
        CatalogOfferSnapshot offer,
        string extraParam,
        int quantity,
        CancellationToken ct
    )
    {
        quantity = Math.Max(1, quantity);

        // Guild furni is bought from the guild pages with the guild id in extraParam; the badge and
        // both recolours have to be baked into the item's stuff data at grant time, because the
        // client renders them straight from there and never asks the server for them again.
        GuildFurniIdentitySnapshot? guildIdentity =
            int.TryParse(extraParam, out int guildId) && guildId > 0
                ? await _grainFactory
                    .GetGroupDirectoryGrain()
                    .GetFurniIdentityAsync((int)this.GetPrimaryKeyLong(), guildId, ct)
                    .ConfigureAwait(true)
                : null;

        // Everything the offer promises, worked out before anything durable happens. Pure and
        // deterministic, so an unknown definition or a malformed product fails here — while there is
        // still nothing to compensate — rather than halfway through the grant.
        FulfillmentPlan plan = _planner.Plan(offer, extraParam, quantity, guildIdentity);

        List<FurnitureEntity> furniEntities =
        [
            .. plan.Furniture.Select(f => new FurnitureEntity
            {
                PlayerEntityId = (int)this.GetPrimaryKeyLong(),
                FurnitureDefinitionEntityId = f.DefinitionId,
                ExtraData = f.ExtraData,
            }),
        ];

        IReadOnlyList<string> badgeCodes = plan.BadgeCodes;
        IReadOnlyList<PetCreateRequest> petRequests = plan.Pets;
        IReadOnlyList<BotCreateRequest> botRequests = plan.Bots;
        IReadOnlyList<PlannedEffect> effectGrants = plan.Effects;
        IReadOnlyList<int> chatStyleGrants = plan.ChatStyleIds;

        List<PetEntity> committedPets = [];
        List<BotEntity> committedBots = [];

        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            dbCtx.AddRange(furniEntities);

            List<string> grantedBadgeCodes = new();

            foreach (string badgeCode in badgeCodes)
            {
                bool alreadyOwned = await dbCtx
                    .PlayerBadges.AnyAsync(
                        b =>
                            b.PlayerEntityId == (int)this.GetPrimaryKeyLong()
                            && b.BadgeCode == badgeCode,
                        ct
                    )
                    .ConfigureAwait(true);

                if (alreadyOwned)
                {
                    continue;
                }

                dbCtx.PlayerBadges.Add(
                    new PlayerBadgeEntity
                    {
                        PlayerEntityId = (int)this.GetPrimaryKeyLong(),
                        BadgeCode = badgeCode,
                        SlotId = 0,
                        PlayerEntity = null!,
                    }
                );

                grantedBadgeCodes.Add(badgeCode);
            }

            // Pets and bots join the same transaction as the furniture and the badges. They used to
            // be committed by CreatePetAsync and CreateBotAsync, one commit each, after this one had
            // already gone through — so an offer carrying several families had four commit
            // boundaries, and a failure at any of them left the earlier families delivered while the
            // wallet refunded the whole price. They are rows written through the same context factory
            // in the same grain: four commits was an artefact of how the code grew, not a constraint
            // anything imposed.
            committedPets = [.. petRequests.Select(BuildPetEntity)];
            committedBots = [.. botRequests.Select(BuildBotEntity)];

            dbCtx.Pets.AddRange(committedPets);
            dbCtx.Bots.AddRange(committedBots);

            // THE PIVOT. Everything the offer promised that this grain owns is durable after this
            // line, and nothing past it may refund the purchase: the goods exist. What remains is
            // cross-grain (avatar effects) or a notification, and both are retried rather than
            // compensated.
            await dbCtx.SaveChangesAsync(ct);

            foreach (FurnitureEntity entity in furniEntities)
            {
                FurnitureDefinitionSnapshot def =
                    _furnitureDefinitionProvider.TryGetDefinition(
                        entity.FurnitureDefinitionEntityId
                    ) ?? throw new VortexException(VortexErrorCodeEnum.FurnitureDefinitionNotFound);

                try
                {
                    await AddFurnitureAsync(
                        new FurnitureItem
                        {
                            ItemId = entity.Id,
                            OwnerId = entity.PlayerEntityId,
                            OwnerName = string.Empty,
                            Definition = def,
                            ExtraData = new ExtraData("{}"),
                            StuffData = _stuffDataFactory.CreateStuffData(
                                (int)StuffDataType.LegacyKey
                            ),
                        },
                        ct
                    );

                    await _events
                        .PublishAsync(
                            new ItemCreatedEvent(
                                entity.Id,
                                entity.PlayerEntityId,
                                JsonSerializer.Serialize(
                                    new
                                    {
                                        source = "catalog",
                                        definitionId = entity.FurnitureDefinitionEntityId,
                                    }
                                )
                            ),
                            ct
                        )
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Furniture {ItemId} was granted to player {PlayerId} but could not be "
                            + "announced.",
                        entity.Id,
                        this.GetPrimaryKeyLong()
                    );
                }
            }

            IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(
                this.GetPrimaryKeyLong()
            );

            foreach (string badgeCode in grantedBadgeCodes)
            {
                try
                {
                    await presence.OnBadgeGrantedAsync(badgeCode, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Badge {BadgeCode} was granted to player {PlayerId} but could not be "
                            + "announced.",
                        badgeCode,
                        this.GetPrimaryKeyLong()
                    );
                }
            }
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }

        // Past the pivot. Every step below either tells the player about something that is already
        // theirs, or grants an effect in another grain — none of it is a reason to take the purchase
        // back, and none of it runs under the request's cancellation token: the client hanging up is
        // the single most common way this used to fail, and it was being read as "undo the sale".
        await AnnounceGrantedFamiliesAsync(
            committedPets,
            committedBots,
            effectGrants,
            chatStyleGrants
        );
    }

    /// <summary>
    /// The post-pivot half of a catalog grant: the notifications for rows that are already committed,
    /// and the avatar effects, which live in another grain.
    /// </summary>
    /// <remarks>
    /// Failures here are logged and swallowed rather than thrown. Throwing would reach the wallet's
    /// shared purchase primitive and refund a purchase whose goods are in the player's inventory —
    /// the exact state this whole change exists to make impossible. A notification that never arrives
    /// costs the player a refresh; a refund of delivered goods costs them the goods.
    /// </remarks>
    private async Task AnnounceGrantedFamiliesAsync(
        List<PetEntity> pets,
        List<BotEntity> bots,
        IReadOnlyList<PlannedEffect> effectGrants,
        IReadOnlyList<int> chatStyleGrants
    )
    {
        // ponytail: CancellationToken.None rather than a host-shutdown token. The requirement is
        // that the request's token cannot reach here; wiring IHostApplicationLifetime into every
        // grain buys the ability to abandon these on a graceful shutdown, which is not obviously
        // what you want for work already owed to a player.
        CancellationToken ct = CancellationToken.None;

        IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(
            this.GetPrimaryKeyLong()
        );

        foreach (PetEntity pet in pets)
        {
            try
            {
                await _events
                    .PublishAsync(
                        new PetAdoptedEvent(
                            (int)this.GetPrimaryKeyLong(),
                            pet.Id,
                            pet.Name,
                            pet.Type
                        ),
                        ct
                    )
                    .ConfigureAwait(true);

                await presence.OnPetAddedToInventoryAsync(ToSnapshot(pet), ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Pet {PetId} was granted to player {PlayerId} but could not be announced.",
                    pet.Id,
                    this.GetPrimaryKeyLong()
                );
            }
        }

        foreach (BotEntity bot in bots)
        {
            try
            {
                // Opens the inventory on top of adding the row: the player just bought this, so
                // showing them where it went is the point.
                await presence
                    .SendComposerAsync(
                        new BotAddedToInventoryEventMessageComposer
                        {
                            Bot = ToSnapshot(bot),
                            OpenInventory = true,
                        }
                    )
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Bot {BotId} was granted to player {PlayerId} but could not be announced.",
                    bot.Id,
                    this.GetPrimaryKeyLong()
                );
            }
        }

        if (effectGrants.Count > 0)
        {
            // The effect grain owns player_effects and pushes AvatarEffectAdded itself.
            IPlayerEffectGrain effects = _grainFactory.GetPlayerEffectGrain(
                this.GetPrimaryKeyLong()
            );

            foreach (PlannedEffect grant in effectGrants)
            {
                try
                {
                    await effects
                        .AddEffectAsync(grant.EffectId, grant.SubType, grant.DurationSeconds, ct)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Effect {EffectId} was bought by player {PlayerId} but could not be granted.",
                        grant.EffectId,
                        this.GetPrimaryKeyLong()
                    );
                }
            }
        }

        if (chatStyleGrants.Count == 0)
        {
            return;
        }

        // Same shape as the effect above: the player grain owns the ownership rows and pushes
        // whichever chat-style message the id's band calls for, so there is nothing to announce here.
        IPlayerGrain player = _grainFactory.GetPlayerGrain((int)this.GetPrimaryKeyLong());

        foreach (int chatStyleId in chatStyleGrants)
        {
            try
            {
                await player.GrantChatStyleAsync(chatStyleId, ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Chat style {ChatStyleId} was bought by player {PlayerId} but could not be granted.",
                    chatStyleId,
                    this.GetPrimaryKeyLong()
                );
            }
        }
    }

    public async Task GrantBadgeAsync(string badgeCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(badgeCode))
        {
            return;
        }

        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            bool alreadyOwned = await dbCtx
                .PlayerBadges.AnyAsync(
                    b =>
                        b.PlayerEntityId == (int)this.GetPrimaryKeyLong()
                        && b.BadgeCode == badgeCode,
                    ct
                )
                .ConfigureAwait(true);

            if (alreadyOwned)
            {
                return;
            }

            dbCtx.PlayerBadges.Add(
                new PlayerBadgeEntity
                {
                    PlayerEntityId = (int)this.GetPrimaryKeyLong(),
                    BadgeCode = badgeCode,
                    SlotId = 0,
                    PlayerEntity = null!,
                }
            );

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(
                this.GetPrimaryKeyLong()
            );

            await presence.OnBadgeGrantedAsync(badgeCode, ct).ConfigureAwait(true);

            await _events
                .PublishAsync(new BadgeGrantedEvent((int)this.GetPrimaryKeyLong(), badgeCode), ct)
                .ConfigureAwait(true);
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }
    }

    public async Task RemoveBadgeAsync(string badgeCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(badgeCode))
        {
            return;
        }

        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            PlayerBadgeEntity? badge = await dbCtx
                .PlayerBadges.FirstOrDefaultAsync(
                    b =>
                        b.PlayerEntityId == (int)this.GetPrimaryKeyLong()
                        && b.BadgeCode == badgeCode,
                    ct
                )
                .ConfigureAwait(true);

            if (badge is null)
            {
                return;
            }

            dbCtx.PlayerBadges.Remove(badge);
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }
    }

    public Task GrantFurnitureDefinitionAsync(
        int definitionId,
        string? extraData,
        CancellationToken ct
    ) => GrantFurnitureDefinitionCopiesAsync(definitionId, extraData, 1, ct);

    public Task GrantFurnitureDefinitionCopiesAsync(
        int definitionId,
        string? extraData,
        int copies,
        CancellationToken ct
    ) =>
        GrantFurnitureDefinitionCopiesAsync(
            definitionId,
            extraData,
            copies,
            CommerceOperationId.None,
            string.Empty,
            ct
        );

    public async Task GrantFurnitureDefinitionCopiesAsync(
        int definitionId,
        string? extraData,
        int copies,
        CommerceOperationId operationId,
        string stepKey,
        CancellationToken ct
    )
    {
        FurnitureDefinitionSnapshot def =
            _furnitureDefinitionProvider.TryGetDefinition(definitionId)
            ?? throw new VortexException(VortexErrorCodeEnum.FurnitureDefinitionNotFound);

        if (copies <= 0)
        {
            return;
        }

        List<FurnitureEntity> entities =
        [
            .. Enumerable
                .Range(0, copies)
                .Select(_ => new FurnitureEntity
                {
                    PlayerEntityId = (int)this.GetPrimaryKeyLong(),
                    FurnitureDefinitionEntityId = def.Id,
                    ExtraData = extraData,
                }),
        ];

        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            dbCtx.AddRange(entities);

            if (!operationId.IsNone)
            {
                dbCtx.CommerceReceipts.Add(
                    new CommerceReceiptEntity
                    {
                        OperationId = operationId.Value,
                        StepKey = stepKey,
                        Result = copies.ToString(CultureInfo.InvariantCulture),
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            // One commit for every copy, and for the receipt that says this step ran. Whatever
            // happens next, the player either has all of them or none, and a replay of the step
            // loses this insert and hands out nothing.
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (DbUpdateException ex) when (!operationId.IsNone)
        {
            // The receipt decides whether this really is a replay. Returning quietly on any write
            // failure is how a grant that never happened gets reported as one that did, and the
            // purchase above it completes over a delivery the player never received.
            if (!await dbCtx.HasCommittedReceiptAsync(operationId, stepKey, ct))
            {
                throw;
            }

            _logger.LogInformation(
                ex,
                "Step {StepKey} of operation {OperationId} already granted {Copies} of definition "
                    + "{DefinitionId} to player {PlayerId}; not granting again.",
                stepKey,
                operationId,
                copies,
                definitionId,
                this.GetPrimaryKeyLong()
            );

            return;
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }

        // Past the commit: cache and client notification for rows that are already the player's.
        // A throw here used to travel back to the wallet's shared purchase primitive and refund a
        // purchase whose furniture was in the database — the caller would have been told the grant
        // failed when what actually failed was telling the player about it. The inventory list is
        // rebuilt from the database on the next reload, so the cost of a lost notification is a
        // refresh; the cost of the refund was the furniture.
        foreach (FurnitureEntity entity in entities)
        {
            try
            {
                await AddFurnitureAsync(
                        new FurnitureItem
                        {
                            ItemId = entity.Id,
                            OwnerId = entity.PlayerEntityId,
                            OwnerName = string.Empty,
                            Definition = def,
                            ExtraData = new ExtraData(extraData ?? "{}"),
                            // Built from the stored blob, exactly like InventoryFurnitureLoader does
                            // on login: a blank legacy default would drop whatever the grant baked in
                            // (a guild badge, a trophy inscription) until the player reconnected.
                            StuffData = _stuffDataFactory.CreateStuffDataFromJson(
                                def.StuffDataType,
                                extraData
                            ),
                        },
                        ct
                    )
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Furniture {ItemId} was granted to player {PlayerId} but could not be added to "
                        + "the live inventory.",
                    entity.Id,
                    this.GetPrimaryKeyLong()
                );
            }
        }
    }

    public async Task GrantFurnitureWithLegacyStuffDataAsync(
        int definitionId,
        string legacyData,
        CancellationToken ct
    )
    {
        ExtraData extraData = new(null);

        extraData.UpdateSection(ExtraDataSectionType.STUFF, new { Data = legacyData });

        await GrantFurnitureDefinitionAsync(definitionId, extraData.GetJsonString(), ct)
            .ConfigureAwait(true);
    }

    public async Task<FurnitureItemSnapshot?> GrantSingleFurnitureIfUnderLimitAsync(
        int definitionId,
        string? extraData,
        int furniLimit,
        CancellationToken ct
    )
    {
        ImmutableArray<FurnitureItemSnapshot> owned = await _furniModule
            .GetAllItemSnapshotsAsync(ct)
            .ConfigureAwait(true);

        if (owned.Length >= furniLimit)
        {
            return null;
        }

        FurnitureDefinitionSnapshot def =
            _furnitureDefinitionProvider.TryGetDefinition(definitionId)
            ?? throw new VortexException(VortexErrorCodeEnum.FurnitureDefinitionNotFound);

        FurnitureEntity entity = new()
        {
            PlayerEntityId = (int)this.GetPrimaryKeyLong(),
            FurnitureDefinitionEntityId = def.Id,
            ExtraData = extraData,
        };

        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            dbCtx.Add(entity);
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }

        FurnitureItem item = new()
        {
            ItemId = entity.Id,
            OwnerId = entity.PlayerEntityId,
            OwnerName = string.Empty,
            Definition = def,
            ExtraData = new ExtraData(extraData ?? "{}"),
            StuffData = _stuffDataFactory.CreateStuffData(StuffDataType.LegacyKey),
        };

        await AddFurnitureAsync(item, ct).ConfigureAwait(true);

        return item.GetSnapshot();
    }

    public async Task GrantLtdFurnitureAsync(
        int furniDefinitionId,
        int serialNumber,
        int seriesSize,
        CancellationToken ct
    )
    {
        FurnitureDefinitionSnapshot def =
            _furnitureDefinitionProvider.TryGetDefinition(furniDefinitionId)
            ?? throw new VortexException(VortexErrorCodeEnum.FurnitureDefinitionNotFound);

        string extraData = $"{{\"serial\":{serialNumber},\"seriesSize\":{seriesSize}}}";

        FurnitureEntity entity = new()
        {
            PlayerEntityId = (int)this.GetPrimaryKeyLong(),
            FurnitureDefinitionEntityId = def.Id,
            ExtraData = extraData,
        };

        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            dbCtx.Add(entity);
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            await AddFurnitureAsync(
                    new FurnitureItem
                    {
                        ItemId = entity.Id,
                        OwnerId = entity.PlayerEntityId,
                        OwnerName = string.Empty,
                        Definition = def,
                        ExtraData = new ExtraData(extraData),
                        StuffData = _stuffDataFactory.CreateStuffData(StuffDataType.LegacyKey),
                    },
                    ct
                )
                .ConfigureAwait(true);

            await _events
                .PublishAsync(
                    new ItemCreatedEvent(
                        entity.Id,
                        entity.PlayerEntityId,
                        JsonSerializer.Serialize(
                            new
                            {
                                source = "ltd",
                                serial = serialNumber,
                                seriesSize,
                            }
                        )
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }
    }

    public Task<FurnitureItemSnapshot?> GetItemSnapshotAsync(
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        return _furniModule.GetItemSnapshotAsync(itemId, ct);
    }

    public Task<ImmutableArray<FurnitureItemSnapshot>> GetAllItemSnapshotsAsync(
        CancellationToken ct
    )
    {
        return _furniModule.GetAllItemSnapshotsAsync(ct);
    }

    public async Task<ImmutableArray<SongDiskSnapshot>> GetSongDisksAsync(CancellationToken ct)
    {
        ImmutableArray<FurnitureItemSnapshot> items = await _furniModule
            .GetAllItemSnapshotsAsync(ct)
            .ConfigureAwait(true);

        ImmutableArray<SongDiskSnapshot>.Builder disks =
            ImmutableArray.CreateBuilder<SongDiskSnapshot>();

        foreach (FurnitureItemSnapshot item in items)
        {
            // The song id is the disk's legacy stuff data — the same string the client reads back as
            // `furniture_extras` and parses as a number. A disk carrying anything else is a disk of
            // nothing: it is skipped rather than reported as song 0, which the client would ask
            // about, never get an answer for, and keep as a nameless entry all session.
            if (item.Definition.LogicName != SoundLogicNames.SongDisk)
            {
                continue;
            }

            int songId = SongDiskExtraData.ReadSongId(
                (item.StuffData as LegacyStuffSnapshot)?.Data
            );

            if (songId <= 0)
            {
                continue;
            }

            disks.Add(new SongDiskSnapshot { DiskId = item.ItemId.Value, SongId = songId });
        }

        return disks.ToImmutable();
    }

    public Task EnsureFurnitureReadyAsync(CancellationToken ct)
    {
        return _furniModule.EnsureFurnitureReadyAsync(ct);
    }

    /// <summary>
    /// Puts a bought offer in a box instead of in the recipient's hands: one present furniture whose
    /// private section names the offer, to be granted for real when they unwrap it.
    /// </summary>
    /// <returns>False when the chosen wrapping names no furniture this hotel ships, so the caller
    /// can fall back to handing the offer over unwrapped rather than swallowing a paid purchase.</returns>
    public async Task<bool> GrantWrappedGiftAsync(
        CatalogOfferSnapshot offer,
        string extraParam,
        GiftWrappingSpec wrapping,
        string purchaserName,
        string purchaserFigure,
        CancellationToken ct
    )
    {
        FurnitureDefinitionSnapshot? present = ResolvePresentDefinition(wrapping.StuffTypeId);

        if (present is null)
        {
            _logger.LogWarning(
                "Gift wrapping stuff type {StuffType} resolves to no present definition; the gift will be granted unwrapped.",
                wrapping.StuffTypeId
            );

            return false;
        }

        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true);

        try
        {
            dbCtx.Add(
                new FurnitureEntity
                {
                    PlayerEntityId = (int)this.GetPrimaryKeyLong(),
                    FurnitureDefinitionEntityId = present.Id,
                    ExtraData = BuildPresentExtraData(
                        offer,
                        extraParam,
                        wrapping,
                        purchaserName,
                        purchaserFigure
                    ),
                }
            );

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(true);
        }

        await _furniModule.ReloadAsync(ct).ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// The wrapping's furniture, named by the class id the gift dialog was given and handed back.
    /// </summary>
    /// <remarks>
    /// This used to read the stuff type as an index into the <c>present_gen*</c> family, clamped to
    /// 1-7. It is not one: the client draws the parcel preview with
    /// <c>getFurnitureImage(stuffType)</c>, so the value only ever meant a furniture class id, and
    /// the clamp turned every id above seven into the same parcel. The two halves now agree because
    /// both read the same list — see <see cref="GiftWrapping"/>.
    /// </remarks>
    private FurnitureDefinitionSnapshot? ResolvePresentDefinition(int stuffTypeId) =>
        GiftWrapping.ResolvePresent(_furnitureDefinitionProvider, stuffTypeId);

    /// <summary>
    /// Both halves of a present in one blob: the "stuff" section every client in the room can read,
    /// and the private section only the server does.
    /// </summary>
    private static string BuildPresentExtraData(
        CatalogOfferSnapshot offer,
        string extraParam,
        GiftWrappingSpec wrapping,
        string purchaserName,
        string purchaserFigure
    )
    {
        ExtraData extraData = new(null);

        // Key names are the client's, from FurniturePresentLogic.setObjectVariables. An anonymous
        // gift omits the sender entirely rather than sending an empty string, because the widget
        // draws whatever it is given.
        Dictionary<string, string> data = new()
        {
            ["MESSAGE"] = wrapping.Message,
            ["PRODUCT_CODE"] =
                offer.Products.Length > 0
                    ? offer.Products[0].FurniDefinitionId.ToString(CultureInfo.InvariantCulture)
                    : string.Empty,
            ["TRUSTED_SENDER"] = "false",
        };

        if (wrapping.ShowPurchaserName)
        {
            data["PURCHASER_NAME"] = purchaserName;
            data["PURCHASER_FIGURE"] = purchaserFigure;
        }

        extraData.UpdateSection(ExtraDataSectionType.STUFF, new { Data = data });
        extraData.UpdateSection(
            ExtraDataSectionType.PRESENT,
            new PresentContentsSnapshot
            {
                OfferId = offer.Id,
                ExtraParam = extraParam,
                Wrapping = FurniturePresentWrapping.Pack(wrapping.BoxTypeId, wrapping.RibbonTypeId),
            }
        );

        return extraData.GetJsonString();
    }
}
