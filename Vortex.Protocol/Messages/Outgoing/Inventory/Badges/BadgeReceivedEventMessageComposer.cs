using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Inventory.Badges;

[GenerateSerializer, Immutable]
public sealed record BadgeReceivedEventMessageComposer : IComposer
{
    /// <summary>Named for what the client reads it as. The emulator has no numeric badge id, so
    /// this stays 0; the client only uses it to key its own badge model.</summary>
    [Id(0)]
    public int BadgeId { get; init; }

    [Id(1)]
    public required string BadgeCode { get; init; }

    /// <summary>How many players own the badge, and its rarity tier — both hotel figures, filled
    /// from the badge directory. The client reads them unconditionally, so leaving them at zero was
    /// not "unknown": it was the claim that the badge is common and nobody else has it.</summary>
    [Id(2)]
    public int OwnerCount { get; init; }

    [Id(3)]
    public int BadgeRarityId { get; init; }
}
