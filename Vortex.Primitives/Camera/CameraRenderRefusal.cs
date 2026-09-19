namespace Vortex.Primitives.Camera;

/// <summary>
/// Why a <c>RenderRoom</c> was not accepted.
/// </summary>
/// <remarks>
/// The client is told none of this: <c>CameraStorageUrl</c> carries a url and nothing else, and an
/// empty one is its whole vocabulary for failure (it alerts <c>camera.render.count.info</c>). The
/// distinctions exist for the server's own log, because "the camera does nothing" is otherwise
/// indistinguishable from a payload that was thirty megabytes, one that named a host on the loopback
/// interface, and one that arrived a day late.
/// </remarks>
public enum CameraRenderRefusal
{
    None = 0,

    /// <summary>No payload at all.</summary>
    Empty,

    /// <summary>The compressed blob is larger than the hotel accepts, refused before inflating.</summary>
    PayloadTooLarge,

    /// <summary>Not readable as zlib, or it ended mid-stream.</summary>
    CorruptPayload,

    /// <summary>Inflating reached the ceiling. The classic compression bomb lands here.</summary>
    SceneTooLarge,

    /// <summary>Inflated cleanly and is not the JSON document the client builds.</summary>
    MalformedScene,

    /// <summary>A field the scene cannot be rendered without is absent.</summary>
    IncompleteScene,

    /// <summary>More planes, sprites, masks or texture columns than the hotel accepts.</summary>
    TooComplex,

    /// <summary>The scene names a room this player is not in.</summary>
    WrongRoom,

    /// <summary>The scene's own timestamp is outside the freshness window — a replay, or a clock.</summary>
    Stale,

    /// <summary>The player has rendered their allowance for the hour.</summary>
    RateLimited,

    /// <summary>The scene was good and this server could not keep it. Not the client's fault,
    /// and the only refusal here that is worth waking somebody for.</summary>
    StorageFailed,
}
