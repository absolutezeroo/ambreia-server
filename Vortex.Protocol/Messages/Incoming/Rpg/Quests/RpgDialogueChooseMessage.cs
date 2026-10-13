using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Quests;

/// <summary>A choice of the dialogue last shown to the player, by its position in that list (spec M2a §3.1, 9019).</summary>
public record RpgDialogueChooseMessage : IMessageEvent
{
    public required int Choice { get; init; }
}
