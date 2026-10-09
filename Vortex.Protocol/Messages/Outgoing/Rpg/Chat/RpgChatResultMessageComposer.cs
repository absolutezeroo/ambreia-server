using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Chat;

/// <summary>A chat message refused (spec F3c §10.2, 9011), to its sender alone: the channel and the
/// code (<c>empty</c>, <c>too_long</c>, <c>rate_limited</c>…). A delivered message gets no result.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgChatResultMessageComposer : IComposer
{
    [Id(0)]
    public required string Channel { get; init; }

    [Id(1)]
    public required string Code { get; init; }
}
