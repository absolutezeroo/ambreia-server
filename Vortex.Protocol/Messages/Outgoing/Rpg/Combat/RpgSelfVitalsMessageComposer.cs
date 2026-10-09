using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>The player's own health and mana, and whether he is in combat (spec M1a §9.2, 9014): the
/// HUD's orbs. Sent to him alone, on entering a zone and whenever one of them changes.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgSelfVitalsMessageComposer : IComposer
{
    [Id(0)]
    public required int Health { get; init; }

    [Id(1)]
    public required int MaxHealth { get; init; }

    [Id(2)]
    public required int Mana { get; init; }

    [Id(3)]
    public required int MaxMana { get; init; }

    [Id(4)]
    public required bool InCombat { get; init; }
}
