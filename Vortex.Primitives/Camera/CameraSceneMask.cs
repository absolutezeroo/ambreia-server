using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// One mask cut out of a room plane — a door or window opening, named by asset key.
/// </summary>
/// <remarks>
/// <see cref="AssetName"/> is a key into the loaded asset index and is never a path fragment. The
/// decoder keeps it as the payload spelled it and the renderer looks it up; nothing concatenates it
/// into a file path, which is the only reason a name of <c>../../</c> is boring here.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraSceneMask
{
    [Id(0)]
    public required string AssetName { get; init; }

    [Id(1)]
    public required CameraScenePoint Location { get; init; }

    [Id(2)]
    public required bool FlipH { get; init; }

    [Id(3)]
    public required bool FlipV { get; init; }
}
