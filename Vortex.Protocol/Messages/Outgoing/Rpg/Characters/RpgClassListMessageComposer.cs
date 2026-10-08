using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>The classes of the served content, starter included (spec F3a §6.2, 9001).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgClassListMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgClassEntry> Classes { get; init; }
}
