using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Quests;

/// <summary>The player talks to an NPC (spec M2a §3.1, 9018).</summary>
public record RpgNpcTalkMessage : IMessageEvent
{
    public required int ObjectId { get; init; }
}
