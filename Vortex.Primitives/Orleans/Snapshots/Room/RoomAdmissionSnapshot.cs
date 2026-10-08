using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Orleans.Snapshots.Room;

/// <summary>A room's answer to a player at its door: refused, or the entry burst to send them.</summary>
[GenerateSerializer, Immutable]
public sealed record RoomAdmissionSnapshot
{
    /// <summary>Why the player stays out; null when admitted.</summary>
    [Id(0)]
    public RoomConnectionErrorType? Refusal { get; init; }

    /// <summary>The room as the client builds it, in the order an official entry sends it.</summary>
    [Id(1)]
    public ImmutableArray<IComposer> Burst { get; init; } = [];
}
