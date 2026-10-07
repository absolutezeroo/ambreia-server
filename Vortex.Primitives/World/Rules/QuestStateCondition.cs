namespace Vortex.Primitives.World.Rules;

public sealed record QuestStateCondition(string Quest, string State) : ZoneCondition;
