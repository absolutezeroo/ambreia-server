using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>Le joueur a gagné un ou plusieurs niveaux d'un coup (M1d décision 1, 9030).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgLevelUpMessageComposer : IComposer
{
    [Id(0)]
    public required int FromLevel { get; init; }

    [Id(1)]
    public required int ToLevel { get; init; }

    [Id(2)]
    public required CharacterStatsSnapshot StatsBefore { get; init; }

    [Id(3)]
    public required CharacterStatsSnapshot StatsAfter { get; init; }

    [Id(4)]
    public required ImmutableArray<string> SkillsLearned { get; init; }
}
