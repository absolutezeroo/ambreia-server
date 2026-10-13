using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>La menace du joueur sur sa cible (M1d §2.1, 9028), à lui seul.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgThreatMessageComposer : IComposer
{
    [Id(0)]
    public required int MonsterObjectId { get; init; }

    /// <summary>floor(menace du joueur / menace du premier × 100), dans 0..100.</summary>
    [Id(1)]
    public required int Percent { get; init; }

    /// <summary>Le joueur est le premier de la table.</summary>
    [Id(2)]
    public required bool HasAggro { get; init; }
}
