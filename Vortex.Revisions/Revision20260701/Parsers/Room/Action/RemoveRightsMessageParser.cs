using System.Collections.Immutable;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Room.Action;

namespace Vortex.Revisions.Revision20260701.Parsers.Room.Action;

internal class RemoveRightsMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int count = packet.PopCount(sizeof(int));
        ImmutableArray<int>.Builder ids = ImmutableArray.CreateBuilder<int>(count);

        for (int i = 0; i < count; i++)
        {
            ids.Add(packet.PopInt());
        }

        return new RemoveRightsMessage { TargetUserIds = ids.ToImmutable() };
    }
}
