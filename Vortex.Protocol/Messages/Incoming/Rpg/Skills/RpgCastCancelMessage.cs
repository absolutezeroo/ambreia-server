using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Skills;

/// <summary>The player stops his own cast or channel (spec M1b §6.4, 9015).</summary>
public record RpgCastCancelMessage : IMessageEvent;
