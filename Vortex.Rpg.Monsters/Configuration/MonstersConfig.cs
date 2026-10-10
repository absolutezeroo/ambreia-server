using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Monsters.Configuration;

/// <summary>Monsters tuning (spec M1c §10), section <c>Vortex:Monsters</c>.</summary>
public sealed class MonstersConfig
{
    public const string SECTION_NAME = "Vortex:Monsters";

    /// <summary>By rank name (<c>elite</c>, <c>boss</c>); a rank absent here is not scaled.</summary>
    // ponytail: the binder adds configured list items to these defaults' immunities; a file that
    // wants fewer must say so per item. Move the defaults to appsettings if that ever bites.
    public Dictionary<string, MonsterRankConfig> Ranks { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["elite"] = new()
            {
                HealthMultiplier = 3,
                DamageMultiplier = 1.5,
                XpMultiplier = 3,
            },
            ["boss"] = new()
            {
                HealthMultiplier = 10,
                DamageMultiplier = 2,
                XpMultiplier = 10,
                Immunities = ["stun", "slow"],
            },
        };

    /// <summary>Summons alive at once around one monster (spec M1c §7.3).</summary>
    public int MaxSummonsPerMonster { get; set; } = 16;

    /// <summary>How long a ranged monster stands still before shooting after it moved.</summary>
    public int RangedAimMs { get; set; } = 250;

    /// <summary>A spawn woken by nearness wakes when a player is this many cells (Chebyshev) or
    /// fewer from a cell of its area (spec Z1 §6.4).</summary>
    public int AoiRadius { get; set; } = 12;

    /// <summary>With nobody near for this long, such a spawn sleeps: its idle monsters go.</summary>
    public int AoiSleepSeconds { get; set; } = 30;

    /// <summary>How often a spawn looks again at its conditions and who is near.</summary>
    public int SpawnConditionCheckMs { get; set; } = 1000;

    public RankScale ScaleFor(MonsterRank rank)
    {
        if (!Ranks.TryGetValue(rank.ToString(), out MonsterRankConfig? r))
        {
            return RankScale.None;
        }

        return new RankScale(
            r.HealthMultiplier,
            r.DamageMultiplier,
            r.XpMultiplier,
            r.Immunities.Select(Parse).OfType<StatusType>().ToFrozenSet()
        );
    }

    /// <summary>What refuses the start, each naming its key.</summary>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [];

        foreach ((string name, MonsterRankConfig r) in Ranks)
        {
            string key = $"{SECTION_NAME}:Ranks:{name}";

            if (
                !Enum.TryParse(name, ignoreCase: true, out MonsterRank _)
                || int.TryParse(name, out _)
            )
            {
                problems.Add($"{key}: '{name}' is not a rank (normal, elite, boss).");
            }

            foreach (
                (string field, double value) in new[]
                {
                    (nameof(r.HealthMultiplier), r.HealthMultiplier),
                    (nameof(r.DamageMultiplier), r.DamageMultiplier),
                    (nameof(r.XpMultiplier), r.XpMultiplier),
                }.Where(f => f.Item2 <= 0)
            )
            {
                problems.Add($"{key}:{field}: {value} is not above 0.");
            }

            foreach (string immunity in r.Immunities.Where(i => Parse(i) is null))
            {
                problems.Add($"{key}:Immunities: '{immunity}' is not a status type.");
            }
        }

        if (MaxSummonsPerMonster < 0 || RangedAimMs < 0)
        {
            problems.Add($"{SECTION_NAME}: MaxSummonsPerMonster and RangedAimMs are not negative.");
        }

        foreach (
            (string field, int value) in new[]
            {
                (nameof(AoiRadius), AoiRadius),
                (nameof(AoiSleepSeconds), AoiSleepSeconds),
                (nameof(SpawnConditionCheckMs), SpawnConditionCheckMs),
            }.Where(f => f.Item2 < 1)
        )
        {
            problems.Add($"{SECTION_NAME}:{field}: {value} is not 1 or more.");
        }

        return problems;
    }

    private static StatusType? Parse(string type) =>
        Enum.TryParse(type, ignoreCase: true, out StatusType parsed) && !int.TryParse(type, out _)
            ? parsed
            : null;
}
