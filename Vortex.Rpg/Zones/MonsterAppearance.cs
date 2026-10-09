namespace Vortex.Rpg.Zones;

/// <summary>How a monster shows in the zone (spec M1a §9.1): its content id as its name, its Habbo
/// pet figure, its level.</summary>
public sealed record MonsterAppearance(string DefinitionId, string Figure, int Level);
