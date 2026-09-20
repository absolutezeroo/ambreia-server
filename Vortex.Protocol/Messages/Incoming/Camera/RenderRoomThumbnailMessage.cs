using System;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Camera;

/// <summary>
/// The same shot, kept as the room's picture instead of the player's.
/// </summary>
/// <remarks>
/// <para>
/// Byte for byte the payload <see cref="RenderRoomMessage"/> carries: the client's
/// <c>RenderRoomThumbnailMessageComposer</c> extends the photo composer and adds nothing but a
/// header, packing in its constructor instead of on send. What differs is entirely on this side —
/// where the picture ends up, and what the player is told about it.
/// </para>
/// <para>
/// A separate record rather than a flag on the other one, because the two are answered by different
/// composers and gated by different rules: a photo costs the player a render from their hourly
/// allowance and comes back as a url, a thumbnail replaces whatever the room had and comes back as
/// a yes or a no.
/// </para>
/// </remarks>
public record RenderRoomThumbnailMessage : IMessageEvent
{
    /// <summary>The deflated scene, exactly as it arrived.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }
}
