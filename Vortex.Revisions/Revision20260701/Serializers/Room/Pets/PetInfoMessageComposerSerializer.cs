using System;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Pets;

internal class PetInfoMessageComposerSerializer(int header)
    : AbstractSerializer<PetInfoMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, PetInfoMessageComposer message)
    {
        PetSnapshot pet = message.Pet;

        packet
            .WriteInteger(pet.PetId)
            .WriteString(pet.Name)
            .WriteInteger(pet.Level)
            .WriteInteger(Math.Max(message.MaxLevel, pet.Level))
            .WriteInteger(pet.Experience)
            .WriteInteger(Math.Max(message.ExperienceRequiredToLevel, pet.Experience))
            .WriteInteger(pet.Energy)
            .WriteInteger(Math.Max(message.MaxEnergy, pet.Energy))
            // Happiness, not nutrition: this pair is the panel's happiness bar. Hunger and thirst
            // never appear in this message at all -- they are the server's business.
            .WriteInteger(pet.Happiness)
            .WriteInteger(Math.Max(message.MaxHappiness, pet.Happiness))
            // respect: figé, retiré avec le client -- pet respects were removed (M0 bis).
            .WriteInteger(0)
            .WriteInteger(pet.OwnerId.Value)
            .WriteInteger(message.Age)
            .WriteString(message.OwnerName)
            .WriteInteger(pet.Race)
            .WriteBoolean(message.HasFreeSaddle)
            .WriteBoolean(message.IsRiding)
            .WriteInteger(message.SkillThresholds.Length);

        foreach (int threshold in message.SkillThresholds)
        {
            packet.WriteInteger(threshold);
        }

        packet
            .WriteInteger(message.AccessRights)
            .WriteBoolean(message.CanBreed)
            .WriteBoolean(message.CanHarvest)
            .WriteBoolean(message.CanRevive)
            .WriteInteger(message.RarityLevel)
            .WriteInteger(message.MaxWellBeingSeconds)
            .WriteInteger(message.RemainingWellBeingSeconds)
            .WriteInteger(message.RemainingGrowingSeconds)
            .WriteBoolean(message.HasBreedingPermission);
    }
}
