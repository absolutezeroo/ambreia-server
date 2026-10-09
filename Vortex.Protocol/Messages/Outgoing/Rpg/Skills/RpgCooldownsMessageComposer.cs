using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>The player's recharges after a cast (spec M1b §6.5, 9022), to him alone.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCooldownsMessageComposer : IComposer
{
    [Id(0)]
    public required int GlobalRemainingMs { get; init; }

    [Id(1)]
    public required ImmutableArray<RpgCooldown> Skills { get; init; }
}
