using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>An NPC and its marker: available, ready, active, or empty.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgNpcMarker
{
    public const string Available = "available";
    public const string Ready = "ready";
    public const string Active = "active";
    public const string None = "";

    [Id(0)]
    public required int ObjectId { get; init; }

    [Id(1)]
    public required string Marker { get; init; }
}
