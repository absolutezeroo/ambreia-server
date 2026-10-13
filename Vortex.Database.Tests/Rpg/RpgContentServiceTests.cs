using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Database.Context;
using Vortex.Database.Tests.Support;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Import;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Migrations;
using Vortex.Rpg.Content.Registry;
using Vortex.Rpg.Content.Rules;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>An import is all or nothing (spec Contenu §6), on SQLite so the transaction is real.</summary>
public sealed class RpgContentServiceTests : IAsyncLifetime
{
    private const int Owner = 7;

    private const string Warrior = """
        { "id": "class.warrior", "kind": "class", "role": "tank", "range": 1, "primaryStat": "strength",
          "base": { "strength": 1, "intelligence": 1, "agility": 1, "health": 1, "mana": 1 }, "perLevel": {},
          "specializations": ["spec.warrior.guardian"] }
        """;

    private const string Guardian = """
        { "id": "spec.warrior.guardian", "kind": "specialization", "class": "class.warrior" }
        """;

    private const string Sword = """
        { "id": "item.weapon.sword", "kind": "item", "category": "equipment", "rarity": "common", "slot": "weapon" }
        """;

    private const string Burning = """
        { "id": "status.burning", "kind": "status", "type": "dot", "durationMs": 6000, "periodMs": 2000, "coefficient": 0.2 }
        """;

    private const string Archer = """
        { "id": "monster.goblin.archer", "kind": "monster", "figure": "8 0 7FA040", "behavior": "ranged",
          "aggressive": true, "rank": "elite", "base": { "health": 30, "damageMin": 1, "damageMax": 2 }, "attackRange": 5 }
        """;

    private const string OldWolf = """
        { "id": "monster.old.wolf", "kind": "monster", "figure": "38 0 FFFFFF", "retired": true,
          "base": { "health": 30, "damageMin": 1, "damageMax": 2 } }
        """;

    private const string Axe = """
        { "id": "item.weapon.axe", "kind": "item", "category": "equipment", "rarity": "common", "slot": "weapon" }
        """;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private RpgContentRegistry _registry = null!;

    /// <summary>What the fake references of items answer, and the replacements it was asked for.</summary>
    private Func<string, bool> _referenced = _ => false;
    private Exception? _replaceFails;
    private readonly List<(string From, string To)> _replaced = [];
    private readonly CommitFaults _commits = new();

    public async Task InitializeAsync()
    {
        (_conn, _) = await SqliteTestDb.OpenAsync();

        // Production runs Pomelo's retrying strategy, which refuses hand-opened transactions and
        // replays the operation after a transient failure.
        _options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseSqlite(
                _conn,
                sqlite => sqlite.ExecutionStrategy(d => new RetryOnTimeoutStrategy(d))
            )
            .AddInterceptors(_commits)
            .Options;

        _registry = new RpgContentRegistry(
            new OptionsDbContextFactory(_options),
            Kinds(),
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            FakeProxy.Create<IEventPublisher>(_ => null),
            NullLogger<RpgContentRegistry>.Instance
        );
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task AnInvalidPack_WritesNothing()
    {
        ContentImportResult result = await ImportAsync(Pack(("class/warrior.json", Warrior)));

        result.Imported.Should().BeFalse();
        result.Findings.Select(f => f.Code).Should().Contain("BrokenReference");
        (await CountAsync()).Should().Be((0, 0));
        _registry.Version.Should().Be(0);
    }

    [Fact]
    public async Task AValidPack_IsWrittenAndServed()
    {
        ContentImportResult result = await ImportAsync(WarriorPack(("item/sword.json", Sword)));

        (result.Imported, result.PackVersion, result.Added).Should().Be((true, 1L, 3));
        (await CountAsync()).Should().Be((3, 1));
        _registry.Get<ItemDefinition>("item.weapon.sword").Should().NotBeNull();
    }

    [Fact]
    public async Task TheSamePackTwice_WritesNothingTheSecondTime()
    {
        await ImportAsync(WarriorPack());

        ContentImportResult again = await ImportAsync(WarriorPack());

        (again.Imported, again.PackVersion, again.Added, again.Changed)
            .Should()
            .Be((true, 1L, 0, 0));
        (await CountAsync()).Should().Be((2, 1));
    }

    [Fact]
    public async Task AChangedDocument_IsUpdated()
    {
        await ImportAsync(WarriorPack());

        ContentImportResult result = await ImportAsync(
            Pack(
                ("class/warrior.json", Warrior.Replace("\"range\": 1", "\"range\": 2")),
                ("specialization/guardian.json", Guardian)
            )
        );

        (result.PackVersion, result.Changed).Should().Be((2L, 1));
        _registry.Get<ClassDefinition>("class.warrior")!.Range.Should().Be(2);
    }

    [Fact]
    public async Task AFileRemoved_IsRetired()
    {
        await ImportAsync(WarriorPack(("item/sword.json", Sword)));

        ContentImportResult result = await ImportAsync(WarriorPack());

        result.Removed.Should().Be(1);
        _registry.Get<ItemDefinition>("item.weapon.sword").Should().BeNull();
        (await CountAsync()).Should().Be((2, 2));
    }

    [Fact]
    public async Task ARemovedFileAddedBack_IsServedAgain()
    {
        await ImportAsync(WarriorPack(("item/sword.json", Sword)));
        await ImportAsync(WarriorPack());

        ContentImportResult result = await ImportAsync(WarriorPack(("item/sword.json", Sword)));

        (result.Imported, result.Added).Should().Be((true, 1));
        _registry.Get<ItemDefinition>("item.weapon.sword").Should().NotBeNull();
    }

    [Fact]
    public async Task RemovingAReferencedId_WithoutARule_IsRefused()
    {
        await ImportAsync(WarriorPack(("item/sword.json", Sword)));
        _referenced = id => id == "item.weapon.sword";

        ContentImportResult result = await ImportAsync(WarriorPack(("item/axe.json", Axe)));

        result.Imported.Should().BeFalse();
        result.Findings.Select(f => f.Code).Should().Contain("ReferencedWithoutMigration");
        (await CountAsync()).Should().Be((3, 1));
    }

    [Fact]
    public async Task AReplaceRule_RewritesTheReferences_Once()
    {
        await ImportAsync(WarriorPack(("item/sword.json", Sword)));
        _referenced = id => id == "item.weapon.sword";
        ContentPack replaced = WarriorPack(
            """[{ "from": "item.weapon.sword", "action": "replace", "to": "item.weapon.axe" }]""",
            ("item/axe.json", Axe)
        );

        ContentImportResult result = await ImportAsync(replaced);
        await ImportAsync(replaced);

        result.Imported.Should().BeTrue();
        _replaced.Should().Equal(("item.weapon.sword", "item.weapon.axe"));
    }

    [Fact]
    public async Task AFailureWhileWriting_RollsEverythingBack()
    {
        await ImportAsync(WarriorPack(("item/sword.json", Sword)));
        _referenced = id => id == "item.weapon.sword";
        _replaceFails = new InvalidOperationException("disk full");

        await FluentActions
            .Awaiting(() =>
                ImportAsync(
                    WarriorPack(
                        """[{ "from": "item.weapon.sword", "action": "replace", "to": "item.weapon.axe" }]""",
                        ("item/axe.json", Axe)
                    )
                )
            )
            .Should()
            .ThrowAsync<InvalidOperationException>();

        (await CountAsync()).Should().Be((3, 1));
        _registry.Version.Should().Be(1);
        _registry.Get<ItemDefinition>("item.weapon.axe").Should().BeNull();
    }

    // Review 7-8: the retry, the client that hangs up, and replace rules across imports.

    [Fact]
    public async Task ACommitRetried_WritesTheWholePack()
    {
        await ImportAsync(WarriorPack(("item/sword.json", Sword)));
        _commits.FailNext = 1;

        ContentImportResult result = await ImportAsync(
            Pack(
                ("class/warrior.json", Warrior.Replace("\"range\": 1", "\"range\": 2")),
                ("specialization/guardian.json", Guardian),
                ("item/axe.json", Axe)
            )
        );

        (result.Imported, result.Added, result.Changed, result.Removed)
            .Should()
            .Be((true, 1, 1, 1));
        await using VortexDbContext db = new(_options);
        (await db.RpgContent.SingleAsync(c => c.ContentId == "class.warrior"))
            .Document.Should()
            .Contain("\"range\":2");
        _registry.Get<ClassDefinition>("class.warrior")!.Range.Should().Be(2);
        _registry.Get<ItemDefinition>("item.weapon.axe").Should().NotBeNull();
        _registry.Get<ItemDefinition>("item.weapon.sword").Should().BeNull();
    }

    [Fact]
    public async Task AClientHangingUpAfterTheCommit_StillGetsTheRegistryReloaded()
    {
        await ImportAsync(WarriorPack());
        using CancellationTokenSource hangUp = new();
        _commits.AfterCommit = hangUp.Cancel;

        await Service().ImportAsync(WarriorPack(("item/axe.json", Axe)), Owner, hangUp.Token);

        _registry.Get<ItemDefinition>("item.weapon.axe").Should().NotBeNull();
        _registry.Version.Should().Be(2);
    }

    [Fact]
    public async Task AReplaceRule_IsAppliedAgain_WhenItsIdIsRemovedAgain()
    {
        string rule =
            """[{ "from": "item.weapon.sword", "action": "replace", "to": "item.weapon.axe" }]""";
        await ImportAsync(WarriorPack(("item/sword.json", Sword), ("item/axe.json", Axe)));
        _referenced = id => id == "item.weapon.sword";

        await ImportAsync(WarriorPack(rule, ("item/axe.json", Axe)));
        await ImportAsync(WarriorPack(rule, ("item/sword.json", Sword), ("item/axe.json", Axe)));
        ContentImportResult again = await ImportAsync(WarriorPack(rule, ("item/axe.json", Axe)));

        again.Removed.Should().Be(1);
        _replaced.Should().HaveCount(2);
    }

    [Fact]
    public async Task AReplaceRuleOfLiveContent_IsNotApplied()
    {
        ContentImportResult result = await ImportAsync(
            WarriorPack(
                """[{ "from": "item.weapon.sword", "action": "replace", "to": "item.weapon.axe" }]""",
                ("item/sword.json", Sword),
                ("item/axe.json", Axe)
            )
        );

        result.Imported.Should().BeTrue();
        _replaced.Should().BeEmpty();
        result.Findings.Select(f => f.Code).Should().Contain("MigrationOfLiveContent");
    }

    [Fact]
    public async Task TheTextsAreKeptPerLanguage()
    {
        ContentPack pack = WarriorPack() with
        {
            Texts = new Dictionary<string, string>
            {
                ["fr"] = FrFor("class.warrior", "spec.warrior.guardian"),
                ["en"] = "class.warrior.name=Warrior",
            },
        };

        await ImportAsync(pack);

        (await Service().GetTextsAsync("fr", CancellationToken.None))
            .Should()
            .Contain("class.warrior.name=");
        (await Service().GetTextsAsync("en", CancellationToken.None))
            .Should()
            .Be("class.warrior.name=Warrior");
        (await Service().GetTextsAsync("de", CancellationToken.None)).Should().BeNull();
    }

    /// <summary>The client reads what it needs to show a skill or a status (its target, range,
    /// times, type) from the served content; a kind that does not say it is public is not served.</summary>
    [Fact]
    public async Task APublicKind_IsServedAsJson_APrivateOrUnknownOneIsNot()
    {
        await ImportAsync(WarriorPack(("status/burning.json", Burning)));

        ContentDefinitions statuses = Service().GetDefinitions("status")!;

        statuses.Version.Should().Be(1);
        JsonNode node = JsonNode.Parse(statuses.Json)!.AsArray().Single()!;
        (
            node["id"]!.GetValue<string>(),
            node["type"]!.GetValue<string>(),
            node["durationMs"]!.GetValue<int>()
        )
            .Should()
            .Be(("status.burning", "dot", 6000));
        Service().GetDefinitions("class").Should().NotBeNull("classes are public: the client reads them (4fceb9fad)");
        Service().GetDefinitions("nope").Should().BeNull();
    }

    /// <summary>The map editor picks a spawn's monster from these (spec M1c §5.1): every monster of
    /// the served content, retired ones flagged, its rank and behaviour as written in the files.</summary>
    [Fact]
    public async Task TheMonsters_AreListed_WithRankBehaviourAndRetirement()
    {
        await ImportAsync(
            WarriorPack(("monster/archer.json", Archer), ("monster/old.json", OldWolf))
        );

        Service()
            .GetMonsters()
            .Should()
            .Equal(
                new MonsterSummary(
                    "monster.goblin.archer",
                    "elite",
                    "ranged",
                    true,
                    "8 0 7FA040",
                    false
                ),
                new MonsterSummary(
                    "monster.old.wolf",
                    "normal",
                    "melee",
                    false,
                    "38 0 FFFFFF",
                    true
                )
            );
    }

    [Fact]
    public async Task TwoImportsAtOnce_AreSerialized()
    {
        RpgContentService service = Service();

        ContentImportResult[] results = await Task.WhenAll(
            Task.Run(() => service.ImportAsync(WarriorPack(), Owner, CancellationToken.None)),
            Task.Run(() =>
                service.ImportAsync(
                    WarriorPack(("item/sword.json", Sword)),
                    Owner,
                    CancellationToken.None
                )
            )
        );

        results.Select(r => r.PackVersion).Order().Should().Equal(1L, 2L);
        (await CountAsync()).Packs.Should().Be(2);
    }

    private static IContentKind[] Kinds() =>
        [
            new ClassKind(),
            new SpecializationKind(),
            new ProgressionKind(),
            new ItemKind(),
            new StatusKind(),
            new MonsterKind(
                FakeProxy.Create<IMonsterBehaviorKeys>(_ =>
                    (IReadOnlySet<string>)new HashSet<string> { "melee", "ranged" }
                )
            ),
        ];

    private RpgContentService Service() =>
        new(
            new OptionsDbContextFactory(_options),
            Kinds(),
            [
                FakeProxy.Create<IContentReferences>(call =>
                    call.Method.Name switch
                    {
                        "get_Kind" => "item",
                        nameof(IContentReferences.IsReferencedAsync) => Task.FromResult(
                            _referenced((string)call.Args![1]!)
                        ),
                        nameof(IContentReferences.ReplaceAsync) => ReplaceAsync(
                            (string)call.Args![1]!,
                            (string)call.Args![2]!
                        ),
                        _ => null,
                    }
                ),
            ],
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            _registry,
            NullLogger<RpgContentService>.Instance
        );

    private Task ReplaceAsync(string from, string to)
    {
        if (_replaceFails is not null)
        {
            return Task.FromException(_replaceFails);
        }

        _replaced.Add((from, to));
        return Task.CompletedTask;
    }

    private Task<ContentImportResult> ImportAsync(ContentPack pack) =>
        Service().ImportAsync(pack, Owner, CancellationToken.None);

    private async Task<(int Live, int Packs)> CountAsync()
    {
        await using VortexDbContext db = new(_options);

        return (
            await db.RpgContent.CountAsync(c => c.DeletedAt == null),
            await db.RpgContentPacks.CountAsync()
        );
    }

    private static ContentPack WarriorPack(params (string Path, string Json)[] more) =>
        WarriorPack(null, more);

    private static ContentPack WarriorPack(
        string? migrations,
        params (string Path, string Json)[] more
    ) =>
        Pack(
            migrations,
            [("class/warrior.json", Warrior), ("specialization/guardian.json", Guardian), .. more]
        );

    private static ContentPack Pack(params (string Path, string Json)[] files) => Pack(null, files);

    private static ContentPack Pack(string? migrations, (string Path, string Json)[] files)
    {
        string[] ids = [.. files.Select(f => f.Json.Split('"')[3])];

        return new ContentPack(
            [.. files.Select(f => new ContentFile(f.Path, f.Json))],
            migrations,
            new Dictionary<string, string> { ["fr"] = FrFor(ids) }
        );
    }

    private static string FrFor(params string[] ids) =>
        string.Join("\n", ids.SelectMany(id => new[] { $"{id}.name=x", $"{id}.description=x" }));
}
