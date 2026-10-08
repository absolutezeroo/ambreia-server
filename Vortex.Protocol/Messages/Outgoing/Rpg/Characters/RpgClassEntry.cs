using System.Collections.Immutable;
using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>A class of the content (spec F3a §6.2); its texts are the keys <c>&lt;id&gt;.name</c>
/// and <c>&lt;id&gt;.description</c> of the client's text files.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgClassEntry
{
    [Id(0)]
    public required string Id { get; init; }

    /// <summary>As in the content: <c>tank</c>, <c>damage</c>, <c>heal</c>.</summary>
    [Id(1)]
    public required string Role { get; init; }

    [Id(2)]
    public required int Range { get; init; }

    /// <summary>As in the content: <c>strength</c>, <c>intelligence</c>, <c>agility</c>.</summary>
    [Id(3)]
    public required string PrimaryStat { get; init; }

    /// <summary>« Sans classe »: never offered by the class picker.</summary>
    [Id(4)]
    public required bool Starter { get; init; }

    [Id(5)]
    public required ImmutableArray<string> Specializations { get; init; }
}
