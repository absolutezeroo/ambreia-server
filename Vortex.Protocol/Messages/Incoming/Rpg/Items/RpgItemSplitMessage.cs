using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Items;

/// <summary>Part of a stack to an empty case (spec Objets §17, 9005).</summary>
public record RpgItemSplitMessage : IMessageEvent
{
    public required int From { get; init; }
    public required int Quantity { get; init; }
    public required int To { get; init; }
}
