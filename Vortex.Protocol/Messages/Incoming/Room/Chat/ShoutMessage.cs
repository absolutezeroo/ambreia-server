using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Room.Chat;

public sealed record ShoutMessage : IMessageEvent
{
    public required string Text { get; init; }
}
