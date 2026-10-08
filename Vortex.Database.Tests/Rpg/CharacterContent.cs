using System;
using System.Collections.Generic;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Tests.Support;

namespace Vortex.Database.Tests.Rpg;

/// <summary>The warrior of content/ and a stand-in registry, for the character tests.</summary>
internal static class CharacterContent
{
    public static readonly ClassDefinition Warrior = new()
    {
        Id = "class.warrior",
        Kind = "class",
        Role = ClassRole.Tank,
        Range = 1,
        ThreatMultiplier = 3,
        PrimaryStat = PrimaryStat.Strength,
        Base = new StatBlock
        {
            Strength = 12,
            Intelligence = 4,
            Agility = 6,
            Health = 60,
            Mana = 40,
            Armor = 4,
        },
        PerLevel = new StatBlock
        {
            Strength = 3,
            Agility = 1,
            Health = 18,
            Mana = 2,
            Armor = 1,
        },
        Specializations = ["spec.warrior.guardian"],
    };

    public static readonly SpecializationDefinition Guardian = new()
    {
        Id = "spec.warrior.guardian",
        Kind = "specialization",
        Class = "class.warrior",
    };

    public static readonly ProgressionDefinition Progression = new()
    {
        Id = "progression.default",
        Kind = "progression",
        MaxLevel = 20,
        XpToNext =
        [
            100,
            150,
            220,
            300,
            400,
            520,
            660,
            820,
            1000,
            1200,
            1450,
            1750,
            2100,
            2500,
            2950,
            3450,
            4000,
            4600,
            5300,
        ],
        HealthPerStrength = 5,
        ManaPerIntelligence = 5,
        CritBasePercent = 5,
        CritPerAgilityPercent = 0.2,
        CritCapPercent = 50,
        DodgePerAgilityPercent = 0.1,
        DodgeCapPercent = 30,
    };

    /// <summary>An <see cref="IRpgContent"/> answering from <paramref name="definitions"/>, read at
    /// each call, at the version <paramref name="version"/> gives.</summary>
    public static IRpgContent Registry(
        IReadOnlyDictionary<string, ContentDefinition> definitions,
        Func<long> version
    ) =>
        FakeProxy.Create<IRpgContent>(call =>
            call.Method.Name switch
            {
                "get_Version" => version(),
                nameof(IRpgContent.Get) => definitions.TryGetValue(
                    (string)call.Args![0]!,
                    out ContentDefinition? d
                ) && call.Method.GetGenericArguments()[0].IsInstanceOfType(d)
                    ? d
                    : null,
                _ => null,
            }
        );
}
