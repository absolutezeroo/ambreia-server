using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;

namespace Vortex.PacketHandlers.Room.Pets;

public class GetPetInfoMessageHandler(
    IGrainFactory grainFactory,
    IPetLevelProvider petLevelProvider
) : IMessageHandler<GetPetInfoMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IPetLevelProvider _petLevelProvider = petLevelProvider;

    public async ValueTask HandleAsync(
        GetPetInfoMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0 || message.PetId <= 0)
        {
            return;
        }

        IRoomPets room = _grainFactory.GetRoomPets(ctx.RoomId);
        PetSnapshot? pet = await room.GetPlacedPetSnapshotAsync(message.PetId, ct)
            .ConfigureAwait(false);

        if (pet is null)
        {
            return;
        }

        IPlayerDirectoryGrain directory = _grainFactory.GetPlayerDirectoryGrain();
        string ownerName = await directory
            .GetPlayerNameAsync(pet.OwnerId, ct)
            .ConfigureAwait(false);

        // The panel draws two bars out of these. None of them used to be sent: the composer's own
        // defaults (100 XP to the next level, 100 energy) were below what a grown pet already had,
        // and the serializer's Math.Max guard then quietly raised each ceiling to the current
        // value -- so every pet showed a full XP bar and a full energy bar whatever its level.
        int maxLevel = _petLevelProvider.GetMaxLevel(pet.Type);
        int nextLevelExperience = _petLevelProvider.GetExperienceForNextLevel(pet.Type, pet.Level);
        int maxEnergy = _petLevelProvider.GetEnergyCapForLevel(pet.Type, pet.Level);

        IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(ctx.PlayerId);
        await presence
            .SendComposerAsync(
                new PetInfoMessageComposer
                {
                    // A pet has no fatigue since M0: the energy bar is always full.
                    Pet = pet with
                    {
                        Energy = maxEnergy,
                    },
                    OwnerName = ownerName,
                    Age = pet.AgeInDays(DateTime.UtcNow),
                    MaxLevel = maxLevel,
                    // At the top of the table there is no next threshold to show, so the bar is
                    // full rather than int.MaxValue wide.
                    ExperienceRequiredToLevel =
                        pet.Level >= maxLevel ? pet.Experience : nextLevelExperience,
                    MaxEnergy = maxEnergy,
                    MaxHappiness = _petLevelProvider.GetNutritionCapForLevel(pet.Type, pet.Level),
                    // The client builds the pet menu from these three alone (OwnPetMenuView,
                    // PetMenuView): "saddled up" -- mount, saddle off, riding permission -- when
                    // the saddle is free and the viewer is not riding; "riding" -- dismount -- when
                    // the viewer is. So the saddle is free only while nobody is on it, riding is
                    // the viewer's own, and the riding-permission checkbox reads accessRights == 1.
                    HasFreeSaddle = pet.HasSaddle && pet.RiderId is null,
                    IsRiding = pet.RiderId == ctx.PlayerId,
                    AccessRights = pet.RidingPermission ? 1 : 0,
                    // Breeding and monsterplants were removed in M0; the client still reads
                    // every field, so they go out at the values the live hotel sends an animal:
                    // no breed, harvest or revive button, rarity 0, and -1 -- the client's own
                    // "unset" -- for the three plant timers.
                    CanBreed = false,
                    CanHarvest = false,
                    CanRevive = false,
                    HasBreedingPermission = false,
                    RarityLevel = 0,
                    MaxWellBeingSeconds = -1,
                    RemainingWellBeingSeconds = -1,
                    RemainingGrowingSeconds = -1,
                }
            )
            .ConfigureAwait(false);
    }
}
