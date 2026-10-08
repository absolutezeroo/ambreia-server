using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>The whole character, for the HUD, on entering a zone (spec F3a §6.2, 9003).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCharacterStateMessageComposer : IComposer
{
    [Id(0)]
    public required int PlayerId { get; init; }

    [Id(1)]
    public required string ClassId { get; init; }

    [Id(2)]
    public required string SpecializationId { get; init; }

    [Id(3)]
    public required int Level { get; init; }

    [Id(4)]
    public required int MaxLevel { get; init; }

    [Id(5)]
    public required int Xp { get; init; }

    /// <summary>XP to reach the next level; 0 at the max level.</summary>
    [Id(6)]
    public required int XpToNext { get; init; }

    [Id(7)]
    public required CharacterStatsSnapshot Stats { get; init; }
}
