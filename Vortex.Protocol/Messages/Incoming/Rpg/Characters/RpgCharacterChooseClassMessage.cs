using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Characters;

/// <summary>A character leaves « Sans classe » for this class, once (spec F3a §6.1, 9002).</summary>
public record RpgCharacterChooseClassMessage : IAccountPhaseMessage
{
    public required int PlayerId { get; init; }
    public required string ClassId { get; init; }
}
