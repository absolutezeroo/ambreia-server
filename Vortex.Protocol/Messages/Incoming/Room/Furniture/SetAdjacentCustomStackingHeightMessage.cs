using Vortex.Primitives.Networking;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Protocol.Messages.Incoming.Room.Furniture;

/// <summary>
/// The up and down arrows of the custom stack-height widget (header 3315).
/// </summary>
/// <remarks>
/// A direction, not a height: every other control on that widget sends an absolute value through
/// <see cref="SetCustomStackingHeightMessage"/>, and these two exist precisely because the step is
/// whatever is stacked on the tile — something only the room can work out
/// (<c>CustomStackHeightWidget.sendAdjacentHeightRequest</c>).
/// </remarks>
public record SetAdjacentCustomStackingHeightMessage : IMessageEvent
{
    public required RoomObjectId ObjectId { get; init; }

    /// <summary>True for the down arrow, false for the up arrow.</summary>
    public required bool Down { get; init; }
}
