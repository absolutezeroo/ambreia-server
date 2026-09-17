using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Pets;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomPetSystem
{
    /// <summary>Breeding dialog states the client understands: the pairing has begun.</summary>
    private const int BreedingStateStarted = 1;

    /// <summary>Result code the client treats as "a pet was produced".</summary>
    private const int BreedingResultSuccess = 1;

    /// <summary>
    /// The nest the pairing happened at. BreedPetsMessage carries only the two pets, so this flow
    /// never learns which nest was used; 0 is sent rather than an id made up on the spot.
    /// </summary>
    private const int UnknownBreedingNestId = 0;

    public async Task TogglePetBreedingPermissionAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        if (!_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet))
        {
            return;
        }

        if (pet.OwnerId != ctx.PlayerId)
        {
            return;
        }

        PetSnapshot updated = pet with { CanBreed = !pet.CanBreed };
        _roomGrain._state.PetsById[petId] = updated;

        if (_motionByPetId.TryGetValue(petId, out PetMotionState? motion))
        {
            motion.IsStatsDirty = true;
        }
    }

    public async Task<bool> BreedPetsAsync(
        ActionContext ctx,
        int petOneId,
        int petTwoId,
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        if (
            !_roomGrain._state.PetsById.TryGetValue(petOneId, out PetSnapshot? petOne)
            || !_roomGrain._state.PetsById.TryGetValue(petTwoId, out PetSnapshot? petTwo)
        )
        {
            await SendBreedingFailureAsync(ctx.PlayerId, 3, ct);
            return false;
        }

        if (petOne.Type != petTwo.Type)
        {
            await SendBreedingFailureAsync(ctx.PlayerId, 1, ct);
            return false;
        }

        if (!petTwo.CanBreed)
        {
            await SendBreedingFailureAsync(ctx.PlayerId, 2, ct);
            return false;
        }

        int proposedRace = Random.Shared.Next(0, 2) == 0 ? petOne.Race : petTwo.Race;
        string proposedColor = BlendColors(petOne.Color, petTwo.Color);
        int proposedGender = Random.Shared.Next(0, 2);

        PendingBreedingSession session = new(
            petOneId,
            petTwoId,
            petOne.OwnerId,
            petTwo.OwnerId,
            proposedRace,
            proposedColor,
            proposedGender
        );

        _breedingByPetOneId[petOneId] = session;

        // The client drives its breeding dialog off a state and the two pets involved; the proposed
        // offspring is settled later and reaches it through ConfirmBreedingRequest.
        PetBreedingEventMessageComposer requesterMsg = new()
        {
            State = BreedingStateStarted,
            OwnPetId = petOneId,
            OtherPetId = petTwoId,
        };

        ConfirmBreedingRequestEventMessageComposer targetMsg = new()
        {
            NestId = UnknownBreedingNestId,
            PetOne = await DescribeBreedingParentAsync(petOne, ct),
            PetTwo = await DescribeBreedingParentAsync(petTwo, ct),
            ResultPetType = proposedRace,
        };

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(petOne.OwnerId)
            .SendComposerAsync(requesterMsg);

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(petTwo.OwnerId)
            .SendComposerAsync(targetMsg);

        return true;
    }

    public async Task<bool> ConfirmPetBreedingAsync(
        ActionContext ctx,
        int nestStuffId,
        string petName,
        int petOneId,
        int petTwoId,
        CancellationToken ct
    )
    {
        // The dialog names both parents, so match the pair in either order rather than guessing
        // from one id. Until the parser was fixed this method received the nest's furniture id and
        // matched nothing, so confirming a breeding silently did nothing at all.
        PendingBreedingSession? session = _breedingByPetOneId.Values.FirstOrDefault(s =>
            (s.PetOneId == petOneId && s.PetTwoId == petTwoId)
            || (s.PetOneId == petTwoId && s.PetTwoId == petOneId)
        );

        if (session is null)
        {
            return false;
        }

        _breedingByPetOneId.Remove(session.PetOneId);

        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        PetEntity baby = new()
        {
            OwnerPlayerEntityId = session.OwnerOneId.Value,
            RoomEntityId = null,
            // The name the player typed in the confirmation dialog. It used to be hardcoded because
            // the packet's name field was never read off the wire.
            Name = string.IsNullOrWhiteSpace(petName) ? "Baby" : petName.Trim(),
            Type = _roomGrain._state.PetsById.TryGetValue(session.PetOneId, out PetSnapshot? p1)
                ? p1.Type
                : 0,
            Race = session.ProposedRace,
            Color = session.ProposedColor,
            Gender = session.ProposedGender == 0 ? AvatarGenderType.Male : AvatarGenderType.Female,
            Level = 1,
            Experience = 0,
            Energy = _roomGrain._roomConfig.Pet.EnergyCap,
            Nutrition = _roomGrain._roomConfig.Pet.NutritionCap,
            Respect = 0,
            X = 0,
            Y = 0,
            Z = 0,
            Direction = (int)Rotation.South,
            ParentOneId = session.PetOneId,
            ParentTwoId = session.PetTwoId,
            OwnerPlayerEntity = null!,
        };

        dbCtx.Pets.Add(baby);
        await dbCtx.SaveChangesAsync(ct);

        // Detached, like the furniture events: the breeder's achievement grain must not be on the
        // path of the confirmation the two players are waiting for. Until this line, breeding was
        // invisible to every progression system in the hotel.
        _roomGrain.PublishDetached(
            new PetBredEvent(session.OwnerOneId, baby.Id, baby.Type, baby.Color)
        );

        // The client keys the outcome to the nest it was started from, and reads a result code
        // rather than a flag: 1 is the success it acts on.
        ConfirmBreedingResultEventMessageComposer resultMsg = new()
        {
            // The real nest, now that the confirmation packet's first field is read. The client
            // keys the result to it, so the placeholder left the dialog waiting on nest 0.
            BreedingNestStuffId = nestStuffId,
            Result = BreedingResultSuccess,
        };

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(session.OwnerOneId)
            .SendComposerAsync(resultMsg);

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(session.OwnerTwoId)
            .SendComposerAsync(resultMsg);

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(session.OwnerOneId)
            .SendComposerAsync(new NestBreedingSuccessEventMessageComposer { NewPetId = baby.Id });

        await _roomGrain.SendComposerToRoomAsync(
            new PetBreedingResultEventMessageComposer
            {
                // The dialog shows one block per parent. Breeding here yields a single pet with
                // no rarity or mutation, so those stay neutral instead of being invented.
                Result = DescribeParent(session.PetOneId, session.OwnerOneId),
                OtherResult = DescribeParent(session.PetTwoId, session.OwnerTwoId),
            }
        );

        return true;
    }

    /// <summary>A parent as the confirmation dialog lists it. The figure is the string form the
    /// client expects here -- type, breed and colour -- not the block used elsewhere.</summary>
    private async Task<PetBreedingParentSnapshot> DescribeBreedingParentAsync(
        PetSnapshot pet,
        CancellationToken ct
    ) =>
        new()
        {
            WebId = pet.PetId,
            Name = pet.Name,
            Level = pet.Level,
            Figure = string.Create(
                CultureInfo.InvariantCulture,
                $"{pet.Type} {pet.Race} {pet.Color}"
            ),
            Owner = await GetOwnerNameAsync(pet.OwnerId, ct),
        };

    /// <summary>One side of the breeding outcome dialog, built from a parent pet.</summary>
    private PetBreedingOutcomeSnapshot DescribeParent(int petId, PlayerId ownerId)
    {
        _roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet);

        return new PetBreedingOutcomeSnapshot
        {
            StuffId = petId,
            ClassId = pet?.Type ?? 0,
            UserId = ownerId.Value,
            UserName = pet?.Name ?? string.Empty,
            RarityLevel = pet?.RarityLevel ?? 0,
        };
    }

    public Task CancelPetBreedingAsync(ActionContext ctx, int petId, CancellationToken ct)
    {
        int keyToRemove = -1;

        foreach (KeyValuePair<int, PendingBreedingSession> kvp in _breedingByPetOneId)
        {
            if (kvp.Key == petId || kvp.Value.PetTwoId == petId)
            {
                keyToRemove = kvp.Key;
                break;
            }
        }

        if (keyToRemove >= 0)
        {
            _breedingByPetOneId.Remove(keyToRemove);
        }

        return Task.CompletedTask;
    }

    private static string BlendColors(string colorA, string colorB)
    {
        if (colorA.Length != 6 || colorB.Length != 6)
        {
            return colorA;
        }

        char[] result = new char[6];

        for (int i = 0; i < 6; i++)
        {
            result[i] = i % 2 == 0 ? colorA[i] : colorB[i];
        }

        return new string(result);
    }

    private async Task SendBreedingFailureAsync(PlayerId playerId, int reason, CancellationToken ct)
    {
        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(new GoToBreedingNestFailureEventMessageComposer { Reason = reason });
    }

    public async Task<PetSnapshot?> PlantMonsterplantSeedAsync(
        ActionContext ctx,
        RoomObjectId seedItemId,
        CancellationToken ct
    )
    {
        await EnsureRoomReadyForPetPlacementAsync(ct);

        if (!_roomGrain._state.ItemsById.TryGetValue(seedItemId, out IRoomItem? seedItem))
        {
            return null;
        }

        int x = seedItem.X;
        int y = seedItem.Y;
        double z = seedItem.Z;
        PlayerId ownerId = seedItem.OwnerId;

        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        PetEntity plant = new()
        {
            OwnerPlayerEntityId = ownerId.Value,
            OwnerPlayerEntity = null!,
            RoomEntityId = _roomGrain.RoomId.Value,
            Name = "Monsterplant",
            Type = MonsterplantPetType,
            Race = 0,
            Color = "ffffff",
            Gender = AvatarGenderType.Male,
            Level = 1,
            Experience = 0,
            Energy = _roomGrain._roomConfig.Pet.EnergyCap,
            Nutrition = _roomGrain._roomConfig.Pet.NutritionCap,
            Respect = 0,
            RarityLevel = Random.Shared.Next(1, 8),
            LastWateredAt = DateTime.UtcNow,
            X = x,
            Y = y,
            Z = z,
            Direction = (int)Rotation.South,
        };

        dbCtx.Pets.Add(plant);

        FurnitureEntity seedEntity = new() { Id = seedItemId.Value };
        dbCtx.Attach(seedEntity);
        seedEntity.DeletedAt = DateTime.UtcNow;
        dbCtx.Entry(seedEntity).Property(f => f.DeletedAt).IsModified = true;

        await dbCtx.SaveChangesAsync(ct);

        PetSnapshot snapshot = RoomPetRuntime.ToSnapshot(plant);
        _roomGrain._state.PetsById[plant.Id] = snapshot;

        await SendPetAddedAsync(snapshot, ct);

        await _roomGrain.ObjectModule.RemoveObjectAsync(ctx, seedItem, ct, ownerId);

        await _roomGrain._events.PublishAsync(
            new ItemDeletedEvent(
                seedItemId.Value,
                ownerId.Value,
                ctx.PlayerId == PlayerId.Invalid ? null : ctx.PlayerId.Value,
                ItemDeletionReason.MonsterplantSeedPlanted
            ),
            ct
        );

        return snapshot;
    }
}
