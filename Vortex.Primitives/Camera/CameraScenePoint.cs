using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>A point in the photo's own pixel space, as the client wrote it.</summary>
[GenerateSerializer, Immutable]
public readonly record struct CameraScenePoint(
    [property: Id(0)] double X,
    [property: Id(1)] double Y
);
