using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;

/// <summary>One decor object a player can click, and what clicking it does (spec F3b §7.2).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgZoneInteractive
{
    [Id(0)]
    public required int ObjectId { get; init; }

    /// <summary><c>door</c>, <c>lever</c>, <c>sign</c>, <c>teleport</c>, <c>sit</c>, <c>use</c> or
    /// <c>interact</c> (an Interact trigger listens to it).</summary>
    [Id(1)]
    public required string Interaction { get; init; }
}
