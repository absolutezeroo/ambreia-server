namespace Vortex.Primitives.World.Rules;

public sealed record HasItemCondition(string Item, int Count) : ZoneCondition;
