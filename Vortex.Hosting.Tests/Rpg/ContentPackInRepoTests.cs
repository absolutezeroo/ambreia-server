using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Hosting.Tests.Architecture;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Configuration;
using Vortex.Rpg.Quests.Objectives;
using Vortex.Rpg.Quests.Rewards;
using Vortex.Rpg.Skills.Effects;
using Xunit;

namespace Vortex.Hosting.Tests.Rpg;

/// <summary>
/// The pack in content/ is what scripts/content/push.mjs sends: it validates with no finding at all,
/// errors or warnings (spec unifiée §20, cohérence).
/// </summary>
public sealed class ContentPackInRepoTests
{
    /// <summary>The throne, the one furniture the demo items place.</summary>
    private const int Throne = 230;

    [Fact]
    public void TheRepositoryPack_IsValid()
    {
        ValidatedPack pack = Validated();

        pack.Findings.Select(f => $"{f.Severity} {f.Code} {f.File} {f.ContentId}: {f.Message}")
            .Should()
            .BeEmpty();
        pack.Definitions.Should().HaveCountGreaterThan(15);
        // Spec M1b §16: three skills per class, and the statuses they apply; spec M1c §15: six
        // monster skills, two statuses and four monsters more.
        pack.Definitions.Values.OfType<SkillDefinition>().Should().HaveCount(19);
        pack.Definitions.Values.OfType<StatusDefinition>().Should().HaveCount(9);
        pack.Definitions.Values.OfType<MonsterDefinition>().Should().HaveCount(6);
    }

    /// <summary>Reads content/ as push.mjs does: documents by path relative to content/, the
    /// migrations file, one text file per language.</summary>
    private static ContentPack Read()
    {
        string root = Path.Combine(RepositoryPaths.Root(), "content");
        string migrations = Path.Combine(root, "migrations.json");

        return new ContentPack(
            [
                .. Directory
                    .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                    .Where(f => f != migrations)
                    .Order()
                    .Select(f => new ContentFile(
                        Path.GetRelativePath(root, f).Replace('\\', '/'),
                        File.ReadAllText(f)
                    )),
            ],
            File.ReadAllText(migrations),
            Directory
                .EnumerateFiles(Path.Combine(root, "texts"), "*.txt")
                .ToDictionary(f => Path.GetFileNameWithoutExtension(f), File.ReadAllText)
        );
    }

    [Fact]
    public void EverySkillAndStatus_NamesItsIcon()
    {
        ValidatedPack pack = Validated();

        pack.Definitions.Values.OfType<SkillDefinition>()
            .Should()
            .OnlyContain(s => s.Icon.Length > 0, "the client maps nothing any more (M1d §6.1)");
        pack.Definitions.Values.OfType<StatusDefinition>()
            .Should()
            .OnlyContain(s => s.Icon.Length > 0);
        pack.Definitions.Values.OfType<SkillDefinition>()
            .Should()
            .ContainSingle(s => s.Id == "skill.cleric.resurrect")
            .Which.Effects.Single()
            .Kind.Should()
            .Be("resurrect");
    }

    /// <summary>The pack validated with every kind the host declares.</summary>
    private static ValidatedPack Validated() =>
        new ContentPackValidator([
            new ClassKind(),
            new SpecializationKind(),
            new ProgressionKind(),
            new ItemKind(),
            // The behaviors MonstersModule declares: a new shipped behavior is added here too.
            new MonsterKind(
                new DeclaredBehaviors([
                    new MeleeBehavior(),
                    new RangedBehavior(Options.Create(new MonstersConfig())),
                    new CasterBehavior(),
                ])
            ),
            // The effect rules SkillsModule declares: a new shipped effect is added here too.
            new SkillKind([
                new DamageEffect(),
                new HealEffect(),
                new ApplyStatusEffect(),
                new InterruptEffect(),
                new DispelEffect(),
                new ThreatEffect(),
                new ResurrectEffect(),
            ]),
            new StatusKind(),
            // The objective and reward kinds QuestsModule declares: a new shipped kind is added here too.
            new QuestKind(
                [
                    new KillObjective(),
                    new ReachObjective(),
                    new InteractObjective(),
                    new TalkObjective(),
                    new CollectObjective(),
                ],
                [new XpReward(), new ItemsReward()]
            ),
        ]).Validate(Read(), id => id == Throne);
}
