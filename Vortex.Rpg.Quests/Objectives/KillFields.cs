namespace Vortex.Rpg.Quests.Objectives;

/// <summary>kill's fields: at least one filter; all given must match.</summary>
public sealed record KillFields(string? Monster = null, string? Zone = null, string? Rank = null);
