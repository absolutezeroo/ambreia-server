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

        const int monsterplantPetType = 16;
        const int monsterplantMaxWellBeingSeconds = 86_400;
        const int monsterplantMaxLevel = 7;

        bool isPlant = pet.Type == monsterplantPetType;

        int remainingWellBeingSeconds = 0;
        if (isPlant && pet.LastWateredAt.HasValue)
        {
            remainingWellBeingSeconds = Math.Max(
                0,
                (int)(
                    monsterplantMaxWellBeingSeconds
                    - (DateTime.UtcNow - pet.LastWateredAt.Value).TotalSeconds
                )
            );
        }

        // The panel draws two bars out of these. None of them used to be sent: the composer's own
        // defaults (100 XP to the next level, 100 energy) were below what a grown pet already had,
        // and the serializer's Math.Max guard then quietly raised each ceiling to the current
        // value -- so every pet showed a full XP bar and a full energy bar whatever its level.
        int maxLevel = _petLevelProvider.GetMaxLevel(pet.Type);
        int nextLevelExperience = _petLevelProvider.GetExperienceForNextLevel(pet.Type, pet.Level);

        IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain(ctx.PlayerId);
        await presence
            .SendComposerAsync(
                new PetInfoMessageComposer
                {
                    Pet = pet,
                    OwnerName = ownerName,
                    Age = pet.AgeInDays(DateTime.UtcNow),
                    MaxLevel = maxLevel,
                    // At the top of the table there is no next threshold to show, so the bar is
                    // full rather than int.MaxValue wide.
                    ExperienceRequiredToLevel =
                        pet.Level >= maxLevel ? pet.Experience : nextLevelExperience,
                    MaxEnergy = _petLevelProvider.GetEnergyCapForLevel(pet.Type, pet.Level),
                    MaxHappiness = _petLevelProvider.GetNutritionCapForLevel(pet.Type, pet.Level),
                    // Plant-only: the live hotel sends false for an animal even when its owner has
                    // breeding switched on. See RoomPetRuntime.CanBreed.
                    CanBreed = isPlant && pet.Level >= monsterplantMaxLevel && pet.CanBreed,
                    // Spent seed charge => no harvest button, see RoomPetRuntime.CanHarvest.
                    CanHarvest =
                        isPlant
                        && pet.Level >= monsterplantMaxLevel
                        && pet.Energy > 0
                        && pet.CanBreed,
                    CanRevive = isPlant && pet.Energy == 0,
                    HasBreedingPermission = !isPlant && pet.CanBreed,
                    // The client only reads a rarity at all for a monsterplant, and only when it is
                    // not negative; the live hotel sends 0 for an animal.
                    RarityLevel = isPlant ? pet.RarityLevel : 0,
                    // -1, not 0, is what the live hotel sends an animal for the three plant timers:
                    // the client's own "unset" value for them.
                    MaxWellBeingSeconds = isPlant ? monsterplantMaxWellBeingSeconds : -1,
                    RemainingWellBeingSeconds = isPlant ? remainingWellBeingSeconds : -1,
                    RemainingGrowingSeconds = -1,
                }
            )
            .ConfigureAwait(false);
    }
}
