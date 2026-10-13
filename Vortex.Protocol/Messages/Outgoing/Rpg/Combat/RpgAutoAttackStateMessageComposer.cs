using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>L'attaque auto du joueur a changé d'état (M1d §2.3, 9031).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgAutoAttackStateMessageComposer : IComposer
{
    [Id(0)]
    public required bool Active { get; init; }
}
