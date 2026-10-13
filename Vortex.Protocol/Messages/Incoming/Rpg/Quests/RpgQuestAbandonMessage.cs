using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Quests;

/// <summary>The player drops a quest under way (spec M2a §3.1, 9020).</summary>
public record RpgQuestAbandonMessage : IMessageEvent
{
    public required string QuestId { get; init; }
}
