using System.Globalization;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Handshake;

namespace Vortex.Revisions.Revision20260701.Serializers.Handshake;

internal class UserObjectMessageSerializer(int header)
    : AbstractSerializer<UserObjectMessage>(header)
{
    /// <summary>
    /// The shape the official server sends, read off a login capture. No consumer of
    /// <c>lastAccessDate</c> exists in WIN63-202607011411, so nothing parses this back -- which is
    /// why the value may still be the creation date rather than a real last access.
    /// </summary>
    private const string LastAccessDateFormat = "dd-MM-yyyy HH:mm:ss";

    protected override void Serialize(IServerPacket packet, UserObjectMessage message)
    {
        packet.WriteInteger(message.Player.PlayerId);
        packet.WriteString(message.Player.Name);
        packet.WriteString(message.Player.Figure);
        packet.WriteString(message.Player.Gender.ToLegacyString());
        packet.WriteString(message.Player.Motto);
        packet.WriteString(message.Player.Name); // real name
        packet.WriteBoolean(false); // direct mail
        // respectTotal, respectLeft, petRespectLeft: figé, retiré avec le client -- respects were
        // removed (M0 bis). Zero is also what keeps them off: AvatarMenuView draws "respect" only
        // for respectLeft > 0 and OwnPetMenuView the scratch button only for petRespectLeft > 0.
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteBoolean(false); // stream publishing enabled

        // `lastAccessDate` on the client side. CurrentCulture made these bytes depend on the host's
        // locale -- the same build sent "18-09-2026 11:06:35" from a dev box and "9/18/2026
        // 11:06:35 AM" from an invariant container. The official server's format is fixed, and a
        // capture of it (evidence/captures/login-official-001.json) is what names it.
        packet.WriteString(
            message.Player.CreatedAt.ToString(LastAccessDateFormat, CultureInfo.InvariantCulture)
        );
        packet.WriteBoolean(false); // can name change
        packet.WriteBoolean(false); // account safety locked (frozen: the lock left with the cut)

        // Two optional groups, each behind its own `bytesAvailable > 0` in the parser
        // (unknowns/_SafePkg_1820/_SafeCls_1819.parse). The second cannot be reached without the
        // first, so the trade lock and the name colour are written even though neither is used
        // here -- they are the toll for the replenish counters behind them.
        packet.WriteBoolean(false); // account trade locked
        packet.WriteString(string.Empty); // name colour
        // respectReplenishesLeft, maxRespectPerDay: figé, retiré avec le client (M0 bis).
        packet.WriteInteger(0);
        packet.WriteInteger(0);
    }
}
