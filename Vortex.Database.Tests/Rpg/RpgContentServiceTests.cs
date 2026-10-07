using System;
using System.Collections.Generic;
using System.Linq;
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

    public async Task InitializeAsync()
    {
        (_conn, _) = await SqliteTestDb.OpenAsync();

        // Production runs Pomelo's retrying strategy, which refuses hand-opened transactions.
        _options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseSqlite(_conn, sqlite => sqlite.ExecutionStrategy(d => new RetryingStrategy(d)))
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
        [new ClassKind(), new SpecializationKind(), new ProgressionKind(), new ItemKind()];

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
                        nameof(IContentReferences.ReplaceAsync) => Replace(
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

    private Task Replace(string from, string to)
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
