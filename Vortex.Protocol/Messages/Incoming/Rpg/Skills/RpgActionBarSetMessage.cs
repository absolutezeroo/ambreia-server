using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Skills;

/// <summary>The player puts an action in a slot of his bar, or clears it with an empty action
/// (spec M1b §9, 9014).</summary>
public record RpgActionBarSetMessage : IMessageEvent
{
    public required int Slot { get; init; }

    /// <summary><c>skill:&lt;id&gt;</c>, or empty.</summary>
    public required string ActionId { get; init; }
}
