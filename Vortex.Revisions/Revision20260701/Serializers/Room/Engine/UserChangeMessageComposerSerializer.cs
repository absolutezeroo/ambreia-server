using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Engine;

/// <summary>
///     The RPG UserChange is five fields. AS3 reads three more after the achievement score (a string,
///     a count-prefixed list of int triplets, the badge rank) and discards or never fills them; the
///     RPG client stops at the score, so they are not written.
/// </summary>
internal class UserChangeMessageComposerSerializer(int header)
    : AbstractSerializer<UserChangeMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, UserChangeMessageComposer message)
    {
        packet
            .WriteInteger(message.ObjectId)
            .WriteString(message.Figure)
            .WriteString(message.Gender.ToLegacyString())
            .WriteString(message.CustomInfo)
            .WriteInteger(message.AchievementScore);
    }
}
