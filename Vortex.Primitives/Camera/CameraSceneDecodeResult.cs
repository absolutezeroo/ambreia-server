using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// Either a scene or the reason there is none. Never both, and never an exception: a malformed
/// payload is the expected case here, not an exceptional one, and a decoder that throws on it hands
/// the caller a choice between catching everything and crashing the session.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CameraSceneDecodeResult
{
    [Id(0)]
    public required CameraScene? Scene { get; init; }

    [Id(1)]
    public required CameraRenderRefusal Refusal { get; init; }

    public bool Accepted => Scene is not null;

    public static CameraSceneDecodeResult Ok(CameraScene scene) =>
        new() { Scene = scene, Refusal = CameraRenderRefusal.None };

    public static CameraSceneDecodeResult Refused(CameraRenderRefusal refusal) =>
        new() { Scene = null, Refusal = refusal };
}
