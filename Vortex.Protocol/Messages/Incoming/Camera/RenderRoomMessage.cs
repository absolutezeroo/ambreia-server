using System;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Camera;

/// <summary>
/// The shot itself: one zlib-compressed description of everything on screen.
/// </summary>
/// <remarks>
/// <para>
/// The client renders the photo locally into a <c>BitmapData</c> and never uploads it. What it sends
/// instead is the scene — the room's planes, every sprite drawn over them, and the camera effects the
/// player turned on — for the server to rasterise into a file it can serve back by url.
/// </para>
/// <para>
/// One argument, a <c>ByteArray</c>, which this protocol writes as an int32 length followed by the
/// raw bytes. Nothing here interprets them: they are hostile input until the scene decoder has said
/// otherwise, and a parser is the wrong place to decide that.
/// </para>
/// </remarks>
public record RenderRoomMessage : IMessageEvent
{
    /// <summary>The deflated scene, exactly as it arrived.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }
}
