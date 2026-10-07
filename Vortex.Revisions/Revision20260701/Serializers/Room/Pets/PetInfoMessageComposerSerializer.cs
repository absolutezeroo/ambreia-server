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
            // energy and happiness: figés au maximum -- a companion has no needs (M0 bis), so both
            // bars are drawn full until the client drops them.
            .WriteInteger(message.MaxEnergy)
            .WriteInteger(message.MaxEnergy)
            .WriteInteger(message.MaxHappiness)
            .WriteInteger(message.MaxHappiness)
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
