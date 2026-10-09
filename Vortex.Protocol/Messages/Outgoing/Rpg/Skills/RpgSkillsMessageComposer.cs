using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>The skills the player knows and his action bar (spec M1b §9, 9018), to him alone.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgSkillsMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<string> Known { get; init; }

    /// <summary>Every slot, empty ones as "".</summary>
    [Id(1)]
    public required ImmutableArray<string> Bar { get; init; }
}
