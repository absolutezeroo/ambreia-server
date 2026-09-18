using System;
using Orleans;

namespace Vortex.Primitives.Rooms;

[GenerateSerializer, Immutable]
public sealed record RoomAdvertisementSnapshot
{
    [Id(0)]
    public required RoomId RoomId { get; init; }

    [Id(1)]
    public required string Name { get; init; }

    [Id(2)]
    public string? Description { get; init; }

    [Id(3)]
    public required int CategoryId { get; init; }

    [Id(4)]
    public required DateTime ExpiresAt { get; init; }

    /// <summary>The purchased slot's own id. The client reads it as the advertisement id, which is
    /// not the room id -- a room can have been advertised many times.</summary>
    [Id(5)]
    public required int AdvertisementId { get; init; }

    /// <summary>When the slot was purchased. The client wants the age of the advertisement rather
    /// than a timestamp, and derives a creation date from it against its own clock.</summary>
    [Id(6)]
    public required DateTime CreatedAt { get; init; }

    /// <summary>Owner of the advertised room. The client shows the advertisement next to whoever
    /// put it up, so both the id and the name travel with it.</summary>
    [Id(7)]
    public required int OwnerPlayerId { get; init; }

    [Id(8)]
    public required string OwnerName { get; init; }
}
