using Vortex.Primitives.Packets;
using Vortex.Primitives.Quests.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Quest;

namespace Vortex.Revisions.Revision20260701.Serializers.Quest;

/// <summary>The whole board: a count, then that many task blocks.</summary>
internal class DailyTasksActiveListMessageComposerSerializer(int header)
    : AbstractSerializer<DailyTasksActiveListMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        DailyTasksActiveListMessageComposer message
    )
    {
        packet.WriteInteger(message.Tasks.Length);

        foreach (DailyTaskSnapshot task in message.Tasks)
        {
            DailyTaskWriter.Write(packet, task);
        }
    }
}

/// <summary>
/// A single task's patch: id, repeats, status, seconds left. The status is a byte here too — the
/// client reads it with readByte, exactly as it does inside the full task block.
/// </summary>
internal class DailyTasksTaskUpdateMessageComposerSerializer(int header)
    : AbstractSerializer<DailyTasksTaskUpdateMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        DailyTasksTaskUpdateMessageComposer message
    )
    {
        packet
            .WriteLong(message.TaskId)
            .WriteInteger(message.Repeats)
            .WriteByte((byte)message.Status)
            .WriteInteger(message.SecondsLeft);
    }
}
