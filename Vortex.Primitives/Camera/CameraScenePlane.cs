using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// One floor or wall plane of the room, as four corner points plus the textures painted onto it.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CameraScenePlane
{
    [Id(0)]
    public required double Z { get; init; }

    /// <summary>Exactly four, in the order the client wrote them. A plane with any other count is
    /// not a plane and the scene is refused rather than repaired.</summary>
    [Id(1)]
    public required ImmutableArray<CameraScenePoint> CornerPoints { get; init; }

    [Id(2)]
    public required int Color { get; init; }

    [Id(3)]
    public required ImmutableArray<CameraSceneMask> Masks { get; init; }

    /// <summary>Columns of asset keys, painted left to right. Keys, never paths.</summary>
    [Id(4)]
    public required ImmutableArray<ImmutableArray<string>> TextureColumns { get; init; }

    [Id(5)]
    public required bool BottomAligned { get; init; }
}
