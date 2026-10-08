using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Characters;

/// <summary>The character screen asks for the account's characters again (spec F3a §6.1, 9000).</summary>
public record RpgCharacterListRequestMessage : IAccountPhaseMessage;
