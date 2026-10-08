using Orleans;

namespace Vortex.Primitives.Rpg.Items;

/// <summary>One item of a grant: which definition, how many.</summary>
[GenerateSerializer, Immutable]
public sealed record ItemGrantLine
{
    [Id(0)]
    public required string DefinitionId { get; init; }

    [Id(1)]
    public required int Quantity { get; init; }
}
