namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>The melee monster (spec M1a §5.3): it closes to its attack's reach, then casts what its
/// rules allow and strikes. Everything else is <see cref="MonsterBehavior"/>'s.</summary>
public sealed class MeleeBehavior : MonsterBehavior
{
    public override string Key => "melee";
}
