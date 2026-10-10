using System.Linq;
using System.Threading.Tasks;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Spawns;

namespace Vortex.Rpg.Monsters.Tests.Support;

/// <summary>
/// A boss on paper: born at (1, 1) (aggro 4, leash 8), its phases at 66 % (bite, enraged, two wolves
/// of level 3, a yell) and 33 % (frenzy, a yell), the statuses and casts recorded. Player 7 (avatar 1)
/// stands at (2, 1) unless told otherwise.
/// </summary>
internal sealed class BossWorld
{
    public static readonly MonsterSkillRule Bite = new() { Skill = "skill.monster.bite" };

    public static readonly MonsterDefinition Boss = MonsterWorld.Wolf with
    {
        Id = "monster.test.boss",
        Phases =
        [
            new MonsterPhase
            {
                AtHealthPercent = 66,
                Skills = [Bite],
                Status = "status.enraged",
                Summon = new MonsterSummon
                {
                    Monster = MonsterWorld.Wolf.Id,
                    Count = 2,
                    Level = 3,
                },
                Yell = "monster.test.boss.phase2",
            },
            new MonsterPhase
            {
                AtHealthPercent = 33,
                Status = "status.frenzy",
                Yell = "monster.test.boss.phase3",
            },
        ],
    };

    public MonsterWorld World { get; } = new();

    public RecordingStatuses Statuses { get; } = new();

    public RecordingCasting Casting { get; } = new();

    public int BossId => World.Zone.Spawned[0].ObjectId;

    public Combatant BossCombatant => World.Simulation.Combat.Combatants[BossId];

    public LiveMonster BossMonster => World.Simulation.Spawner.Live[BossId];

    public int[] SummonIds => [.. World.Zone.Spawned.Skip(1).Select(s => s.ObjectId)];

    public async Task StartAsync(MonsterDefinition? boss = null, int playerX = 2)
    {
        MonsterDefinition b = boss ?? Boss;
        World.Start(
            [MonsterWorld.WolvesAt(1, (1, 1)) with { Monster = b.Id }],
            null,
            [Casting, Statuses],
            b
        );
        await World.EnterAsync(7, 1, playerX, 1);
        await World.TickAsync();
    }

    /// <summary>Its health to that share, then one tick.</summary>
    public async Task HurtToAsync(int percent)
    {
        BossCombatant.Health = BossCombatant.MaxHealth * percent / 100;
        World.Zone.NowMs += 250;
        await World.TickAsync();
    }
}
