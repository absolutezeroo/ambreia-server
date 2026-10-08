using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Engine;

internal class UserUpdateMessageComposerSerializer(int header)
    : AbstractSerializer<UserUpdateMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, UserUpdateMessageComposer message)
    {
        packet.WriteInteger(message.Avatars.Length);

        foreach (RoomAvatarSnapshot avatar in message.Avatars)
        {
            packet
                .WriteInteger(avatar.ObjectId)
                .WriteInteger(avatar.X)
                .WriteInteger(avatar.Y)
                .WriteString(avatar.Z.ToString())
                .WriteInteger((int)avatar.HeadRotation)
                .WriteInteger((int)avatar.BodyRotation)
                // NO jumpingPower int here. The July Flash build reads one between the body
                // rotation and the status (_SafePkg_2184/_SafeCls_2826.parse:85) but the hotel
                // serves a Nitro/Helium client, whose UserUpdateParser.ts goes straight from the
                // two rotations to the status string. Writing the int shifted the status, so `mv`
                // never parsed and the walk animation died on a live server.
                // Wire truth here is Nitro's TypeScript parser, not the AS3.
                .WriteString(avatar.Status);
        }
    }
}
