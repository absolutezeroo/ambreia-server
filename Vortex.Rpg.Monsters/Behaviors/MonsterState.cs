namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>Where a monster's AI is (spec M1a §5.3). Death is the combat's: a dead monster is gone.</summary>
public enum MonsterState
{
    Idle = 1,
    Chase = 2,
    Fight = 3,
    Return = 4,
}
