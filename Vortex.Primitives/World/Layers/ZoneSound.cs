namespace Vortex.Primitives.World.Layers;

public sealed record ZoneSound(
    string Id,
    ZoneCell Cell,
    string Sound,
    int Radius,
    int Volume,
    bool Loop
);
