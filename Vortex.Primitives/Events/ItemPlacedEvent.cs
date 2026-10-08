namespace Vortex.Primitives.Events;

/// <summary>A furniture item was placed from inventory into a room.</summary>
/// <param name="IsWallItem">
/// Whether it went on a wall rather than the floor. The coarse type an operator means by "a floor
/// item", which no definition id expresses on its own.
/// </param>
/// <param name="LogicName">The definition's logic key, e.g. <c>furniture_floor_hole</c>.</param>
public sealed record ItemPlacedEvent(
    int ItemId,
    int ActorPlayerId,
    int OwnerId,
    int RoomId,
    string? Data,
    int DefinitionId = 0,
    bool IsWallItem = false,
    string LogicName = ""
) : IEvent;
