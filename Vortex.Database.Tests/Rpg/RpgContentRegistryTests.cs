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
using Vortex.Database.Entities.Rpg;
using Vortex.Database.Tests.Support;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Registry;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>The registry rebuilds its content from the tables, and keeps the old one when the new
/// one is invalid.</summary>
public sealed class RpgContentRegistryTests : IAsyncLifetime
{
    private const int Throne = 230;

    private const string Warrior = """
        { "id": "class.warrior", "kind": "class", "role": "tank", "range": 1, "primaryStat": "strength",
          "base": { "strength": 1, "intelligence": 1, "agility": 1, "health": 1, "mana": 1 }, "perLevel": {},
          "specializations": ["spec.warrior.guardian"] }
        """;

    private const string Guardian = """
        { "id": "spec.warrior.guardian", "kind": "specialization", "class": "class.warrior" }
        """;

    private const string ThroneItem = """
        { "id": "item.furniture.throne", "kind": "item", "category": "furniture", "rarity": "common",
          "furnitureDefinition": 230 }
        """;

    private readonly List<IEvent> _events = [];

    /// <summary>Runs inside validation, between a reload's reads and its swap.</summary>
    private Action _onFurnitureLookup = () => { };
    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private RpgContentRegistry _registry = null!;

    public async Task InitializeAsync()
    {
        (_conn, _options) = await SqliteTestDb.OpenAsync();

        IFurnitureDefinitionProvider furniture = FakeProxy.Create<IFurnitureDefinitionProvider>(
            call =>
            {
                _onFurnitureLookup();

                return
                    call.Method.Name == nameof(IFurnitureDefinitionProvider.TryGetDefinition)
                    && (int)call.Args![0]! == Throne
                    ? TestDefinitions.Floor(Throne, "throne", 1, 1)
                    : null;
            }
        );
        IEventPublisher events = FakeProxy.Create<IEventPublisher>(call =>
        {
            lock (_events)
            {
                _events.Add((IEvent)call.Args![0]!);
            }

            return null;
        });

        _registry = new RpgContentRegistry(
            new OptionsDbContextFactory(_options),
            [new ClassKind(), new SpecializationKind(), new ProgressionKind(), new ItemKind()],
            furniture,
            events,
            NullLogger<RpgContentRegistry>.Instance
        );
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task AnEmptyDatabase_GivesAnEmptyRegistry()
    {
        await _registry.ReloadAsync(CancellationToken.None);

        _registry.Version.Should().Be(0);
        _registry.All<ClassDefinition>().Should().BeEmpty();
    }

    [Fact]
    public async Task StoredContent_IsServedByIdAndByKind()
    {
        await StoreAsync(("class.warrior", Warrior), ("spec.warrior.guardian", Guardian));

        await _registry.ReloadAsync(CancellationToken.None);

        _registry.Get<ClassDefinition>("class.warrior").Should().NotBeNull();
        _registry.Get<ItemDefinition>("class.warrior").Should().BeNull();
        _registry
            .All<SpecializationDefinition>()
            .Select(s => s.Id)
            .Should()
            .Equal("spec.warrior.guardian");
        _registry.Version.Should().Be(1);
    }

    [Fact]
    public async Task InvalidStoredContent_RefusesToLoad()
    {
        await StoreAsync(
            (
                "item.weapon.sword",
                """{ "id": "item.weapon.sword", "kind": "item", "category": "equipment", "rarity": "common", "slot": "weapon", "allowedClasses": ["class.paladin"] }"""
            )
        );

        await FluentActions
            .Awaiting(() => _registry.ReloadAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<ContentInvalidException>()
            .WithMessage("*class.paladin*");
    }

    [Fact]
    public async Task AFailedReload_KeepsThePreviousContent()
    {
        await StoreAsync(("class.warrior", Warrior), ("spec.warrior.guardian", Guardian));
        await _registry.ReloadAsync(CancellationToken.None);

        await using (VortexDbContext db = new(_options))
        {
            await db
                .RpgContent.Where(c => c.ContentId == "spec.warrior.guardian")
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Document, "{ nope"));
        }

        await FluentActions
            .Awaiting(() => _registry.ReloadAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<ContentInvalidException>();
        _registry.Get<SpecializationDefinition>("spec.warrior.guardian").Should().NotBeNull();
    }

    [Fact]
    public async Task AnItemOnAFurniture_LoadsAfterTheCatalogue()
    {
        await StoreAsync(("item.furniture.throne", ThroneItem));

        await _registry.ReloadAsync(CancellationToken.None);

        _registry.LoadStage.Should().Be(1);
        _registry
            .Get<ItemDefinition>("item.furniture.throne")!
            .FurnitureDefinition.Should()
            .Be(Throne);
    }

    [Fact]
    public async Task AReload_PublishesWhatChanged()
    {
        await StoreAsync(("class.warrior", Warrior), ("spec.warrior.guardian", Guardian));
        await _registry.ReloadAsync(CancellationToken.None);

        await using (VortexDbContext db = new(_options))
        {
            await db
                .RpgContent.Where(c => c.ContentId == "class.warrior")
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.Document, Warrior.Replace("\"range\": 1", "\"range\": 2"))
                );
        }

        await _registry.ReloadAsync(CancellationToken.None);

        _events
            .OfType<ContentReloadedEvent>()
            .Should()
            .Equal(new ContentReloadedEvent(1, 2, 0, 0), new ContentReloadedEvent(1, 0, 1, 0));
    }

    [Fact]
    public async Task TwoReloadsAtOnce_AreSerialized()
    {
        await StoreAsync(("item.furniture.throne", ThroneItem));
        using ManualResetEventSlim firstInside = new();
        using ManualResetEventSlim release = new();
        int lookups = 0;
        _onFurnitureLookup = () =>
        {
            if (Interlocked.Increment(ref lookups) == 1)
            {
                firstInside.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        };

        // The first reload has read version 1 and is validating it; version 2 lands meanwhile.
        Task first = Task.Run(() => _registry.ReloadAsync(CancellationToken.None));
        firstInside.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        await using (VortexDbContext db = new(_options))
        {
            RpgContentPackEntity pack = new()
            {
                AccountId = 1,
                Added = 1,
                Changed = 0,
                Removed = 0,
            };
            db.RpgContentPacks.Add(pack);
            await db.SaveChangesAsync();
            db.RpgContent.Add(
                new RpgContentEntity
                {
                    ContentId = "item.furniture.chair",
                    Kind = "item",
                    Document = ThroneItem.Replace("item.furniture.throne", "item.furniture.chair"),
                    Hash = new string('0', 64),
                    PackId = pack.Id,
                }
            );
            await db.SaveChangesAsync();
        }

        Task second = Task.Run(() => _registry.ReloadAsync(CancellationToken.None));
        // Unserialized, the second reload swaps version 2 in here, and the first then puts 1 back.
        await Task.WhenAny(second, Task.Delay(300));
        release.Set();
        await Task.WhenAll(first, second);

        _registry.Version.Should().Be(2);
        _registry.Get<ItemDefinition>("item.furniture.chair").Should().NotBeNull();
    }

    private async Task StoreAsync(params (string Id, string Json)[] rows)
    {
        await using VortexDbContext db = new(_options);

        RpgContentPackEntity pack = new()
        {
            AccountId = 1,
            Added = rows.Length,
            Changed = 0,
            Removed = 0,
        };
        db.RpgContentPacks.Add(pack);
        await db.SaveChangesAsync();

        foreach ((string id, string json) in rows)
        {
            db.RpgContent.Add(
                new RpgContentEntity
                {
                    ContentId = id,
                    Kind = id[..id.IndexOf('.')],
                    Document = json,
                    Hash = new string('0', 64),
                    PackId = pack.Id,
                }
            );
        }

        db.RpgContentTexts.Add(
            new RpgContentTextEntity
            {
                Language = "fr",
                Body = string.Join(
                    "\n",
                    rows.SelectMany(r => new[] { $"{r.Id}.name=x", $"{r.Id}.description=x" })
                ),
            }
        );
        await db.SaveChangesAsync();
    }
}
