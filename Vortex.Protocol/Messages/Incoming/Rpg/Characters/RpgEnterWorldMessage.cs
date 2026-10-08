using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Characters;

/// <summary>The socket leaves the character screen and plays this character (spec F3a §6.1, 9003).</summary>
public record RpgEnterWorldMessage : IAccountPhaseMessage
{
    public required int PlayerId { get; init; }
}
