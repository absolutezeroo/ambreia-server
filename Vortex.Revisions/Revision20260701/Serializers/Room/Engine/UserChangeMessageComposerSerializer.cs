using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Engine;

/// <summary>
///     The client reads eight fields here, not five. After the achievement score it takes a string,
///     then a length-prefixed list of int triplets, then the badge rank — and it discards all three
///     without looking at them, which is why their meaning is not recoverable from the client and
///     why the last two are written empty below.
///
///     Writing only the first five made every read past the score run off the end of the buffer, so
///     the client threw <c>End of buffer</c> and dropped the whole message: a figure or motto change
///     made outside the room never reached anyone already in it. Same failure shape as the missing
///     badge rank in <see cref="Data.RoomAvatarSerializer" /> — one short field does not degrade, it
///     kills the packet.
/// </summary>
internal class UserChangeMessageComposerSerializer(int header)
    : AbstractSerializer<UserChangeMessageComposer>(header)
{
    /// <summary>
    ///     The trailing string the client reads and throws away. Its meaning is unknown — the client
    ///     never assigns it — so an empty string is written rather than a guessed value.
    /// </summary>
    private const string UnusedTrailingField = "";

    /// <summary>
    ///     The client reads this many int triplets and discards them. Nothing on the server has a
    ///     list to put here, so none are sent.
    /// </summary>
    private const int TripletCount = 0;

    /// <summary>
    ///     Badge rank. Nothing in the server computes one yet, and the client's "nobody ranked me"
    ///     value is -1, not 0: it draws the rank row for anything <c>>= 0</c>
    ///     (infostand/InfoStandUserView.as:638-646), so zero put a "#0" and a leaderboard link on
    ///     every avatar in the hotel.
    /// </summary>
    private const int BadgesRank = -1;

    protected override void Serialize(IServerPacket packet, UserChangeMessageComposer message)
    {
        packet
            .WriteInteger(message.ObjectId)
            .WriteString(message.Figure)
            .WriteString(message.Gender.ToLegacyString())
            .WriteString(message.CustomInfo)
            .WriteInteger(message.AchievementScore)
            .WriteString(UnusedTrailingField)
            .WriteInteger(TripletCount)
            .WriteInteger(BadgesRank);
    }
}
