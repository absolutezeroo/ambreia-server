using System.IO;
using System.Linq;
using FluentAssertions;
using Vortex.Hosting.Tests.Architecture;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
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
        ValidatedPack pack = new ContentPackValidator([
            new ClassKind(),
            new SpecializationKind(),
            new ProgressionKind(),
            new ItemKind(),
            new MonsterKind(ShippedBehaviorKey.All),
            // The effect rules SkillsModule declares: a new shipped effect is added here too.
            new SkillKind([
                new DamageEffect(),
                new HealEffect(),
                new ApplyStatusEffect(),
                new InterruptEffect(),
                new DispelEffect(),
                new ThreatEffect(),
            ]),
            new StatusKind(),
        ]).Validate(Read(), id => id == Throne);

        pack.Findings.Select(f => $"{f.Severity} {f.Code} {f.File} {f.ContentId}: {f.Message}")
            .Should()
            .BeEmpty();
        pack.Definitions.Should().HaveCountGreaterThan(15);
        // Spec M1b §16: three skills per class, and the statuses they apply.
        pack.Definitions.Values.OfType<SkillDefinition>().Should().HaveCount(12);
        pack.Definitions.Values.OfType<StatusDefinition>().Should().HaveCount(7);
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
}
