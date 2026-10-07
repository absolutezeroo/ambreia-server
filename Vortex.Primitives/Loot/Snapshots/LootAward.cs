using Orleans;

namespace Vortex.Primitives.Loot.Snapshots;

/// <summary>
/// A prize that has actually been granted, in the shape the reward window needs:
/// <see cref="ContentType"/> is the legacy product code ("s"/"i"/"e"/"h") and
/// <see cref="ClassId"/> the sprite id (floor/wall) or effect id the client draws.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record LootAward
{
    [Id(0)]
    public required string ContentType { get; init; }

    [Id(1)]
    public required int ClassId { get; init; }
}
