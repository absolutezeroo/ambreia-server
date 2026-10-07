using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Room.Chat;

public sealed record WhisperMessage : IMessageEvent
{
    public required string Text { get; init; }
    public required string RecipientName { get; init; }
}
