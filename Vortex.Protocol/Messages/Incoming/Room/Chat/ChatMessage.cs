using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Room.Chat;

public sealed record ChatMessage : IMessageEvent
{
    public required string Text { get; init; }
    public required int TrackingId { get; init; }
}
