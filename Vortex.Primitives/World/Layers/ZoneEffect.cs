namespace Vortex.Primitives.World.Layers;

public sealed record ZoneEffect(string Id, ZoneCell Cell, string Effect, double Scale, int Radius);
