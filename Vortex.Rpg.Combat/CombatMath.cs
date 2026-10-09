using System;
using Vortex.Rpg.Combat.Configuration;

namespace Vortex.Rpg.Combat;

/// <summary>Distance, cadence and XP (spec M1a §6.1, §8.2).</summary>
public static class CombatMath
{
    /// <summary>Distance in tiles, diagonals counting one (spec §6.3).</summary>
    public static int Chebyshev(int ax, int ay, int bx, int by) =>
        Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    /// <summary>Time between two hits of a player at this speed, never below the floor.</summary>
    public static int AttackIntervalMs(int speedPercent, CombatConfig config) =>
        Math.Max(
            config.MinAttackIntervalMs,
            config.PlayerAttackIntervalMs * 100 / Math.Max(1, speedPercent)
        );

    /// <summary>A monster's XP for one player: ±10 % per level of difference, +50 % at most, none
    /// from a monster 8 levels below or more.</summary>
    public static int XpFor(int monsterXp, int monsterLevel, int playerLevel, CombatConfig config)
    {
        int gap = monsterLevel - playerLevel;

        if (gap <= -config.XpCutoffLevels)
        {
            return 0;
        }

        int percent = Math.Min(
            100 + gap * config.XpLevelStepPercent,
            100 + config.XpMaxBonusPercent
        );

        return Math.Max(0, monsterXp * percent / 100);
    }
}
