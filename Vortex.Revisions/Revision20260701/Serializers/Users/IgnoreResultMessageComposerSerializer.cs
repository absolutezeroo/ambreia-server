using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Revisions.Revision20260701.Serializers.Users;

internal class IgnoreResultMessageComposerSerializer(int header)
    : AbstractSerializer<IgnoreResultMessageComposer>(header)
{
    /// <remarks>
    /// The result code comes first. <c>IgnoreResultMessageEventParser.parse()</c> reads
    /// <c>result</c> then <c>ignoredUserId</c>, and <c>IgnoredUsersManager.onIgnoreResult()</c>
    /// switches on the first of the two. Written the other way round, the client switched on the
    /// player id: id 2 took the "list full" arm and added player 1 to the ignore list, id 3 took
    /// the unignore arm and removed one. It only ever looked right for player 1, where the swap
    /// is invisible.
    /// </remarks>
    protected override void Serialize(IServerPacket packet, IgnoreResultMessageComposer message)
    {
        packet.WriteInteger(message.ResultCode);
        packet.WriteInteger(message.UserId);
    }
}
