namespace Vortex.Primitives.World.Rules;

public sealed record TimeOfDayCondition(int From, int To) : ZoneCondition;
