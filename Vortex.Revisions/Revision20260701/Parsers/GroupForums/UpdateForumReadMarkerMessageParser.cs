using System.Collections.Immutable;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.GroupForums;

namespace Vortex.Revisions.Revision20260701.Parsers.GroupForums;

internal class UpdateForumReadMarkerMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int count = packet.PopCount(sizeof(int) + sizeof(int) + sizeof(bool));

        ImmutableArray<ForumReadMarkerUpdate>.Builder markers =
            ImmutableArray.CreateBuilder<ForumReadMarkerUpdate>(count);

        for (int i = 0; i < count; i++)
        {
            markers.Add(
                new ForumReadMarkerUpdate(
                    GroupId: packet.PopInt(),
                    ReadMessageCount: packet.PopInt(),
                    MarkAllRead: packet.PopBoolean()
                )
            );
        }

        return new UpdateForumReadMarkerMessage { Markers = markers.ToImmutable() };
    }
}
