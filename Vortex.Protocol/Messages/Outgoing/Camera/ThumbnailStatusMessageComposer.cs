using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Camera;

/// <summary>
/// The answer to <c>RenderRoomThumbnail</c>: whether the room's picture was replaced.
/// </summary>
/// <remarks>
/// Two booleans, and the client's parser defaults <c>ok</c> to <b>true</b> before reading anything —
/// alone among the camera parsers — so an empty body means success. Both are written all the same:
/// a refusal has to say so out loud, and the widget shows a different alert for "you have taken too
/// many" than for everything else.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record ThumbnailStatusMessageComposer : IComposer
{
    [Id(0)]
    public required bool Ok { get; init; }

    /// <summary>
    /// The player is out of renders for this hour. Only consulted when <see cref="Ok"/> is false,
    /// and it is the one refusal the client has words for; every other reason is a silent no.
    /// </summary>
    [Id(1)]
    public required bool RenderLimitHit { get; init; }
}
