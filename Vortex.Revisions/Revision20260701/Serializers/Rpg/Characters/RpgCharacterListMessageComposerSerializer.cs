using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

internal class RpgCharacterListMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCharacterListMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgCharacterListMessageComposer message)
    {
        packet.WriteInteger(message.MaxCharacters).WriteInteger(message.Characters.Length);

        foreach (RpgCharacterListEntry character in message.Characters)
        {
            packet
                .WriteInteger(character.PlayerId)
                .WriteString(character.Name)
                .WriteString(character.Figure)
                .WriteString(character.Gender)
                .WriteString(character.ClassId)
                .WriteString(character.SpecializationId)
                .WriteInteger(character.Level)
                .WriteBoolean(character.Starter);
        }
    }
}
