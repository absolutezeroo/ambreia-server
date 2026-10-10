using Orleans;

namespace Vortex.Primitives.World.Rules;

/// <summary>
/// One part of what a zone knows of a player, kept so its rules can answer on every step (spec Z1
/// §4.3): a module declares its part and how to read it, the zone keeps the latest of each type.
/// <see cref="Sequence"/> only grows for a given player and part: an older reading that arrives
/// late is dropped.
/// </summary>
[GenerateSerializer, Immutable]
public abstract record ZoneFacts
{
    [Id(0)]
    public long Sequence { get; init; }
}
