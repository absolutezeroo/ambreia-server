namespace Vortex.Primitives.Rpg.Content;

/// <summary>What happens to rows that name a removed id (spec Contenu §8). Stored: never renumber.</summary>
public enum ContentMigrationAction
{
    Replace = 0,
    Retire = 1,
}
