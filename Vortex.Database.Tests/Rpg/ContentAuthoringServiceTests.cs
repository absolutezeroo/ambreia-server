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
using Vortex.Primitives.Rpg.Content.Authoring;
using Vortex.Rpg.Content.Authoring;
using Vortex.Rpg.Content.Import;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Migrations;
using Vortex.Rpg.Content.Registry;
using Vortex.Rpg.Content.Rules;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Content authoring §4: one document at a time, through the whole import, on SQLite.</summary>
public sealed class ContentAuthoringServiceTests : IAsyncLifetime
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

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private RpgContentRegistry _registry = null!;
    private Func<string, bool> _held = _ => false;

    public async Task InitializeAsync()
    {
        (_conn, _) = await SqliteTestDb.OpenAsync();
        _options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseSqlite(
                _conn,
                sqlite => sqlite.ExecutionStrategy(d => new RetryOnTimeoutStrategy(d))
            )
            .Options;
        _registry = new RpgContentRegistry(
            new OptionsDbContextFactory(_options),
            Kinds(),
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            FakeProxy.Create<IEventPublisher>(_ => null),
            NullLogger<RpgContentRegistry>.Instance
        );

        ContentImportResult first = await Content()
            .ImportAsync(
                new ContentPack(
                    [
                        new ContentFile("class/warrior.json", Warrior),
                        new ContentFile("specialization/guardian.json", Guardian),
                        new ContentFile("item/sword.json", Sword),
                    ],
                    "[]",
                    new Dictionary<string, string>
                    {
                        ["fr"] =
                            "class.warrior.name=Guerrier\nspec.warrior.guardian.name=Gardien\nitem.weapon.sword.name=Épée\n",
                    }
                ),
                Owner,
                CancellationToken.None
            );

        first.Imported.Should().BeTrue();
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task ADocumentPut_IsCreated_AndReadBackWithItsTexts()
    {
        ContentWriteResult put = await Authoring()
            .PutAsync(Pickaxe(baseVersion: 1), Owner, CancellationToken.None);

        (put.Error, put.Result!.Imported, put.Result.PackVersion, put.Result.Added)
            .Should()
            .Be((null, true, 2L, 1));

        ContentDocument? read = await Authoring()
            .GetAsync("item.tool.pickaxe", CancellationToken.None);

        (read!.Version, read.Kind, read.Path).Should().Be((2L, "item", "item/pickaxe.json"));
        read.Source.Should().Contain("\"category\": \"material\"");
        read.Texts["fr"]
            .Should()
            .Equal(new Dictionary<string, string> { ["item.tool.pickaxe.name"] = "Pioche" });
    }

    [Fact]
    public async Task AWriteOnAnOldVersion_IsStale_AndWritesNothing()
    {
        await Authoring().PutAsync(Pickaxe(baseVersion: 1), Owner, CancellationToken.None);

        ContentWriteResult again = await Authoring()
            .PutAsync(
                Pickaxe(baseVersion: 1) with
                {
                    Source = Pickaxe(1).Source.Replace("material", "equipment"),
                },
                Owner,
                CancellationToken.None
            );

        again.Error.Should().Be(ContentWriteResult.Stale);
        (await Authoring().GetAsync("item.tool.pickaxe", CancellationToken.None))!
            .Source.Should()
            .Contain("material");
    }

    [Fact]
    public async Task AnImportOnAnOldBaseVersion_WritesNothing()
    {
        await Authoring().PutAsync(Pickaxe(baseVersion: 1), Owner, CancellationToken.None);

        ContentImportResult stale = await Content()
            .ImportAsync(
                new ContentPack([], "[]", new Dictionary<string, string>()),
                Owner,
                1,
                CancellationToken.None
            );

        stale.Imported.Should().BeFalse();
        stale.Findings.Select(f => f.Code).Should().Equal("Stale");
        (await Authoring().ListAsync("item", CancellationToken.None))!
            .Documents.Should()
            .HaveCount(2);
    }

    [Fact]
    public async Task AnInvalidDocument_IsRefusedWithTheValidatorsFindings()
    {
        ContentWriteResult put = await Authoring()
            .PutAsync(
                Pickaxe(1) with
                {
                    Source =
                        """{"id":"item.tool.pickaxe","kind":"item","category":"material","rarity":"common","allowedClasses":["class.ghost"]}""",
                },
                Owner,
                CancellationToken.None
            );

        put.Result!.Imported.Should().BeFalse();
        put.Result.Findings.Select(f => f.Code).Should().Contain("BrokenReference");
    }

    [Fact]
    public async Task ARefusedEdit_SaysWhy_BeforeAnyImport() =>
        (
            await Authoring()
                .PutAsync(Pickaxe(1) with { Id = "item.tool.axe" }, Owner, CancellationToken.None)
        )
            .Error.Should()
            .Be(PackEditResult.IdMismatch);

    [Fact]
    public async Task AnUnknownKind_IsRefused() =>
        (
            await Authoring()
                .PutAsync(Pickaxe(1) with { Kind = "spell" }, Owner, CancellationToken.None)
        )
            .Error.Should()
            .Be(ContentWriteResult.UnknownKind);

    [Fact]
    public async Task AHeldItem_IsNotRemoved_WithoutAReplacement()
    {
        _held = id => id == "item.weapon.sword";
        await Authoring().PutAsync(Pickaxe(1), Owner, CancellationToken.None);

        ContentWriteResult refused = await Authoring()
            .DeleteAsync("item.weapon.sword", 2, null, Owner, CancellationToken.None);
        ContentWriteResult replaced = await Authoring()
            .DeleteAsync(
                "item.weapon.sword",
                2,
                "item.tool.pickaxe",
                Owner,
                CancellationToken.None
            );

        refused.Result!.Findings.Select(f => f.Code).Should().Contain("ReferencedWithoutMigration");
        (replaced.Result!.Imported, replaced.Result.Removed).Should().Be((true, 1));
        (await Authoring().GetAsync("item.weapon.sword", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task TheList_IsTheKindsDocuments_WithTheirPaths()
    {
        ContentDocumentList list = (await Authoring().ListAsync("item", CancellationToken.None))!;

        list.Version.Should().Be(1);
        list.Documents.Should()
            .Equal(
                new ContentDocumentSummary("item.weapon.sword", "item", "item/sword.json", false)
            );
        (await Authoring().ListAsync("spell", CancellationToken.None)).Should().BeNull();
    }

    private static ContentWrite Pickaxe(long baseVersion) =>
        new(
            "item.tool.pickaxe",
            "item",
            baseVersion,
            null,
            """{"id":"item.tool.pickaxe","kind":"item","category":"material","rarity":"common"}""",
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["fr"] = new Dictionary<string, string> { ["item.tool.pickaxe.name"] = "Pioche" },
            }
        );

    private ContentAuthoringService Authoring() =>
        new(
            new StoredPackReader(new OptionsDbContextFactory(_options)),
            Content(),
            Kinds(),
            NullLogger<ContentAuthoringService>.Instance
        );

    private RpgContentService Content() =>
        new(
            new OptionsDbContextFactory(_options),
            Kinds(),
            [
                FakeProxy.Create<IContentReferences>(call =>
                    call.Method.Name switch
                    {
                        "get_Kind" => "item",
                        nameof(IContentReferences.IsReferencedAsync) => Task.FromResult(
                            _held((string)call.Args![1]!)
                        ),
                        nameof(IContentReferences.ReplaceAsync) => Task.CompletedTask,
                        _ => null,
                    }
                ),
            ],
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            _registry,
            NullLogger<RpgContentService>.Instance
        );

    private static IContentKind[] Kinds() =>
        [
            new ClassKind(),
            new SpecializationKind(),
            new ProgressionKind(),
            new ItemKind(),
            new StatusKind(),
        ];
}
