namespace Vortex.Rpg.Content.Rules;

/// <summary>A monster behaviour a module declares, seen by its key only: the import refuses a monster
/// whose behaviour nobody declares (spec M1c §5.4).</summary>
public interface IMonsterBehaviorKey
{
    public string Key { get; }
}
