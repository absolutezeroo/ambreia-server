using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Characters;

/// <summary>A new character for the account (spec F3a §6.1, 9001); it is born « Sans classe ».</summary>
public record RpgCharacterCreateMessage : IAccountPhaseMessage
{
    public required string Name { get; init; }
    public required string Figure { get; init; }

    /// <summary><c>M</c> or <c>F</c>.</summary>
    public required string Gender { get; init; }
}
