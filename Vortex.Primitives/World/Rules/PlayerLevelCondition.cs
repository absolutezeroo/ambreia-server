namespace Vortex.Primitives.World.Rules;

public sealed record PlayerLevelCondition(int Min, int Max) : ZoneCondition;
