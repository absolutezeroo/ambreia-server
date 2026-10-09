using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>A combatant's health, for the bars over the avatars (spec M1a §9.2).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCombatantVitals
{
    /// <summary>The room object id.</summary>
    [Id(0)]
    public required int ObjectId { get; init; }

    [Id(1)]
    public required int Health { get; init; }

    [Id(2)]
    public required int MaxHealth { get; init; }
}
