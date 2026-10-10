using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>
/// How a monster thinks (spec M1a §5.3, §13), declared by DI by the module that owns it and found by
/// its key from the monster's content (<c>melee</c>; <c>ranged</c>, <c>caster</c> and bosses come with
/// M1c). The last one declared for a key wins.
/// </summary>
public interface IMonsterBehavior : IMonsterBehaviorKey
{
    /// <summary>One decision per combat tick: state, target, movement.</summary>
    public void Think(MonsterBrain brain);
}
