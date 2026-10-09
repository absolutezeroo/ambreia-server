using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Items;

/// <summary>Moves the item of a bag case to another: moved, merged or swapped (spec Objets §17, 9004).</summary>
public record RpgItemMoveMessage : IMessageEvent
{
    public required int From { get; init; }
    public required int To { get; init; }
}
