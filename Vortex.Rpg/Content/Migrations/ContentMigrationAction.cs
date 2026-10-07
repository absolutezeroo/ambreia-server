namespace Vortex.Rpg.Content.Migrations;

/// <summary>What happens to rows that name a removed id (spec Contenu §8). Stored: never renumber.</summary>
public enum ContentMigrationAction
{
    Replace = 0,
    Retire = 1,
}
