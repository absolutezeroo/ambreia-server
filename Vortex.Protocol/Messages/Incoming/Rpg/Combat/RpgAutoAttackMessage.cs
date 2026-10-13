using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Combat;

/// <summary>Démarre ou arrête l'attaque auto sur la cible courante (M1d §2.2, 9016).</summary>
public record RpgAutoAttackMessage : IMessageEvent
{
    public required bool Active { get; init; }
}
