using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>Un combattant et ce qu'il vise (M1d §2.1).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgTargetEntry
{
    [Id(0)]
    public required int ObjectId { get; init; }

    /// <summary>0 : aucune cible.</summary>
    [Id(1)]
    public required int TargetObjectId { get; init; }
}
