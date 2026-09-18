using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rooms;

namespace Vortex.Protocol.Messages.Outgoing.Navigator;

/// <summary>A room's active advertisement -- sent on room entry when the room has one, and again
/// after a successful EditEventMessage so the client's "my events" panel refreshes without a full
/// re-fetch. See CancelEventMessageHandler for the "Event" naming note.</summary>
/// <remarks>
/// Every field here is one the client reads. It used to carry five and the client parses ten, so
/// the read ran off the end of the buffer: the advertisement id was taken for the room id, the room
/// id for a name length, and nothing after that landed anywhere. Fields are not optional on this
/// message -- there is no bytes-available guard in the parser -- so none of them may be dropped
/// even when it has nothing to say.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record RoomEventMessageComposer : IComposer
{
    /// <summary>The purchased slot's id, which is not the room id.</summary>
    [Id(0)]
    public required int AdvertisementId { get; init; }

    [Id(1)]
    public required int OwnerPlayerId { get; init; }

    [Id(2)]
    public required string OwnerName { get; init; }

    [Id(3)]
    public required RoomId RoomId { get; init; }

    /// <summary>
    /// Read by the client into <c>RoomEventData.eventType</c> and used by nothing, in this client or
    /// in the AS3 one. No reference emulator implements this composer either, so what the official
    /// server puts here is unknown and 0 is an assumption -- but the int has to be written or every
    /// field after it shifts. Kept as a field rather than a literal in the serializer so a real
    /// value can be plumbed the day one is found.
    /// </summary>
    [Id(4)]
    public int EventType { get; init; }

    [Id(5)]
    public required string Name { get; init; }

    [Id(6)]
    public string? Description { get; init; }

    /// <summary>Age of the advertisement, not a timestamp: the client subtracts it from its own
    /// clock to rebuild a creation date.</summary>
    [Id(7)]
    public required int MinutesSinceCreation { get; init; }

    [Id(8)]
    public required int MinutesRemaining { get; init; }

    [Id(9)]
    public required int CategoryId { get; init; }
}
