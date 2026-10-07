using System.Collections.Generic;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneAmbience
{
    public string Tint { get; init; } = "#FFFFFF";
    public int Brightness { get; init; } = 100;
    public bool DayNight { get; init; }
    public List<ZoneLight> Lights { get; init; } = [];
    public List<ZoneSound> Sounds { get; init; } = [];
    public List<ZoneEffect> Effects { get; init; } = [];
}
