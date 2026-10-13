namespace Vortex.Rpg.Combat.Combatants;

/// <summary>Une proposition de résurrection en attente sur un joueur KO (M1d §2.3).</summary>
public sealed record ResurrectOffer(int CasterObjectId, int HealthPercent, long ExpiresAtMs);
