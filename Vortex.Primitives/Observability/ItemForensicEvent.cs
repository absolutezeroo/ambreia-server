namespace Vortex.Primitives.Observability;

/// <summary>
/// A single lifecycle event for one item. The correlation id and timestamp are stamped by the sink at
/// record time. Ownership transitions carry both the previous and the new owner.
/// </summary>
public readonly record struct ItemForensicEvent
{
    public required long ItemId { get; init; }

    public required ItemEventType EventType { get; init; }

    /// <summary>Player who caused the event (placer, mover, picker, ...).</summary>
    public long? ActorPlayerId { get; init; }

    public long? FromOwnerId { get; init; }

    public long? ToOwnerId { get; init; }

    public int? RoomId { get; init; }

    /// <summary>Optional structured (JSON) payload with event-specific detail.</summary>
    public string? Data { get; init; }
}
