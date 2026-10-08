using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>The account's characters (spec F3a §6.2, 9000).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCharacterListMessageComposer : IComposer
{
    [Id(0)]
    public required int MaxCharacters { get; init; }

    [Id(1)]
    public required ImmutableArray<RpgCharacterListEntry> Characters { get; init; }
}
