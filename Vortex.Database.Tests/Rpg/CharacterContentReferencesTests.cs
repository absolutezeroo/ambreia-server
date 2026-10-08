using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Database.Tests.Support;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Content;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Import;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Migrations;
using Vortex.Rpg.Content.Registry;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Characters hold class ids (spec Personnage §12): a removal needs a replace rule, the
/// rule moves them, and an active grain follows without writing the old id back.</summary>
public sealed class CharacterContentReferencesTests : IAsyncLifetime
{
    private const int Player = 7;

    private const string Warrior = """
        { "id": "class.warrior", "kind": "class", "role": "tank", "range": 1, "primaryStat": "strength",
          "base": { "strength": 12, "intelligence": 4, "agility": 6, "health": 60, "mana": 40 }, "perLevel": {},
          "specializations": ["spec.warrior.guardian"] }
        """;

    private const string Guardian = """
        { "id": "spec.warrior.guardian", "kind": "specialization", "class": "class.warrior" }
        """;

    private const string Knight = """
        { "id": "class.knight", "kind": "class", "role": "tank", "range": 1, "primaryStat": "strength",
          "base": { "strength": 14, "intelligence": 4, "agility": 6, "health": 60, "mana": 40 }, "perLevel": {},
          "specializations": ["spec.knight.guardian"] }
        """;

    private const string KnightGuardian = """
        { "id": "spec.knight.guardian", "kind": "specialization", "class": "class.knight" }
        """;

    private const string Progression = """
        { "id": "progression.default", "kind": "progression", "maxLevel": 3, "xpToNext": [100, 150],
          "healthPerStrength": 5, "manaPerIntelligence": 5, "critBasePercent": 5, "critPerAgilityPercent": 0.2,
          "critCapPercent": 50, "dodgePerAgilityPercent": 0.1, "dodgeCapPercent": 30 }
        """;

    private const string ReplaceRules = """
        [{ "from": "class.warrior", "action": "replace", "to": "class.knight" },
         { "from": "spec.warrior.guardian", "action": "replace", "to": "spec.knight.guardian" }]
        """;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private RpgContentRegistry _registry = null!;
    private RpgContentService _service = null!;

    public async Task InitializeAsync()
    {
        (_conn, _options) = await SqliteTestDb.OpenAsync();

        IContentKind[] kinds =
        [
            new ClassKind(),
            new SpecializationKind(),
            new ProgressionKind(),
            new ItemKind(),
        ];
        IFurnitureDefinitionProvider furniture = FakeProxy.Create<IFurnitureDefinitionProvider>(_ =>
            null
        );
        _registry = new RpgContentRegistry(
            new OptionsDbContextFactory(_options),
            kinds,
            furniture,
            FakeProxy.Create<IEventPublisher>(_ => null),
            NullLogger<RpgContentRegistry>.Instance
        );
        _service = new RpgContentService(
            new OptionsDbContextFactory(_options),
            kinds,
            new IContentReferences[] { new ClassReferences(), new SpecializationReferences() },
            furniture,
            _registry,
            NullLogger<RpgContentService>.Instance
        );

        (await ImportAsync(null, Warrior, Guardian)).Imported.Should().BeTrue();

        await using VortexDbContext db = new(_options);
        db.PlayerCharacters.Add(
            new PlayerCharacterEntity
            {
                PlayerEntityId = Player,
                ClassId = "class.warrior",
                SpecializationId = "spec.warrior.guardian",
            }
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task RemovingAHeldClass_WithoutARule_IsRefused() =>
        (await ImportAsync(null, Knight, KnightGuardian))
            .Findings.Select(f => f.Code)
            .Should()
            .Contain("ReferencedWithoutMigration");

    [Fact]
    public async Task AReplaceRule_MovesTheCharacters()
    {
        (await ImportAsync(ReplaceRules, Knight, KnightGuardian)).Imported.Should().BeTrue();

        PlayerCharacterEntity row = await RowAsync();
        (row.ClassId, row.SpecializationId).Should().Be(("class.knight", "spec.knight.guardian"));
    }

    [Fact]
    public async Task AReplacedClass_IsReReadAndNeverWrittenBack()
    {
        PlayerCharacterGrain grain =
            GrainActivationContext.CreateWithIntegerKey<PlayerCharacterGrain>(
                Player,
                new OptionsDbContextFactory(_options),
                _registry,
                new IStatSource[] { new ClassLevelStatSource() },
                FakeProxy.Create<IEventPublisher>(_ => null),
                FakeProxy.Create<ICancellableEventPublisher>(_ =>
                    Task.FromResult(new EventContext { CorrelationId = "test" })
                ),
                Options.Create(new CharacterConfig()),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                NullLogger<PlayerCharacterGrain>.Instance,
                SilentPresence.Grains()
            );
        await grain.OnActivateAsync(CancellationToken.None);
        await grain.GainXpAsync(50, "test");

        (await ImportAsync(ReplaceRules, Knight, KnightGuardian)).Imported.Should().BeTrue();

        (await grain.GetSummaryAsync())!.ClassId.Should().Be("class.knight");
        (await grain.GetStatsAsync())!.Strength.Should().Be(14);
        await grain.OnDeactivateAsync(
            new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"),
            CancellationToken.None
        );
        PlayerCharacterEntity row = await RowAsync();
        (row.ClassId, row.Xp).Should().Be(("class.knight", 50));
    }

    private Task<ContentImportResult> ImportAsync(string? migrations, params string[] documents) =>
        _service.ImportAsync(
            new ContentPack(
                [
                    .. documents
                        .Append(Progression)
                        .Select((d, i) => new ContentFile($"file{i}.json", d)),
                ],
                migrations,
                new Dictionary<string, string> { ["fr"] = "" }
            ),
            1,
            CancellationToken.None
        );

    private async Task<PlayerCharacterEntity> RowAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db
            .PlayerCharacters.AsNoTracking()
            .SingleAsync(c => c.PlayerEntityId == Player);
    }
}
