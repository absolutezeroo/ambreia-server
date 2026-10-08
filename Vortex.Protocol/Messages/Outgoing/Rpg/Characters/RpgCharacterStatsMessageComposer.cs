using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>The character's stats after they changed (spec F3a §6.2, 9005).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCharacterStatsMessageComposer : IComposer
{
    [Id(0)]
    public required CharacterStatsSnapshot Stats { get; init; }
}
