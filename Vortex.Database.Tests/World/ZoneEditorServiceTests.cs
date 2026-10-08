using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Tests.Support;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Editing;
using Vortex.Primitives.World.Layers;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Vortex.World;
using Xunit;

namespace Vortex.Database.Tests.World;

/// <summary>Save, publish, roll back and lock, on SQLite so the transaction is real.</summary>
public sealed class ZoneEditorServiceTests : IAsyncLifetime
{
    private const int Author = 11;
    private const int Other = 12;

    private readonly List<RoomId> _reloaded = [];
    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private ZoneEditorService _service = null!;

    public async Task InitializeAsync()
    {
        (_conn, _) = await SqliteTestDb.OpenAsync();

        // Production runs Pomelo's retrying strategy, which refuses hand-opened transactions.
        _options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseSqlite(_conn, sqlite => sqlite.ExecutionStrategy(d => new RetryingStrategy(d)))
            .Options;

        await using VortexDbContext db = new(_options);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO players (id, name, figure, gender, status, perk_flags, created_at, updated_at) "
                + "VALUES (1, '__world__', 'hd-180-1', 0, 0, 0, datetime('now'), datetime('now'))"
        );
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO room_models (id, name, model, door_x, door_y, door_rotation, enabled, custom, created_at, updated_at) "
                + "VALUES (1, 'model_a', '000\r000', 0, 0, 2, 1, 0, datetime('now'), datetime('now'))"
        );

        UseConfig(new WorldConfig());
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task SavingThenPublishing_MovesThePointer_AndReloadsTheRoom()
    {
        ZoneSummary zone = await _service.CreateAsync("village", "Village", Author, null, default);
        (await _service.TryLockAsync("village", Author, false, default)).Should().BeTrue();

        ZoneSaveResult saved = await _service.SaveAsync(
            "village",
            Json(),
            Author,
            "first",
            default
        );

        saved.Saved.Should().BeTrue();
        saved.Published.Should().BeFalse();

        ZoneSaveResult published = await _service.PublishAsync(
            "village",
            saved.Version!.Value,
            default
        );

        published.Published.Should().BeTrue();
        (await _service.ListAsync(default)).Single().PublishedVersion.Should().Be(1);
        _reloaded.Should().Equal(new RoomId(zone.RoomId));
    }

    [Fact]
    public async Task AnInvalidDocument_IsNotSaved()
    {
        await CreateLockedAsync();

        ZoneSaveResult result = await _service.SaveAsync(
            "village",
            "{\"schemaVersion\":1}",
            Author,
            "",
            default
        );

        result.Saved.Should().BeFalse();
        result.Findings.Should().Contain(f => f.Code == "Json");
        (await _service.GetVersionsAsync("village", default)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnotherAccount_CannotSaveWhileTheLockIsHeld()
    {
        await CreateLockedAsync();

        (await _service.TryLockAsync("village", Other, false, default)).Should().BeFalse();
        (await _service.SaveAsync("village", Json(), Other, "", default))
            .Findings.Should()
            .Contain(f => f.Code == "Locked");
        (await _service.TryLockAsync("village", Other, true, default)).Should().BeTrue();
    }

    [Fact]
    public async Task RollingBack_PublishesAnEarlierVersion()
    {
        await CreateLockedAsync();
        await _service.SaveAsync("village", Json(), Author, "v1", default);
        await _service.SaveAsync("village", Json("Village 2"), Author, "v2", default);
        await _service.PublishAsync("village", 2, default);

        await _service.PublishAsync("village", 1, default);

        (await _service.GetDocumentAsync("village", null, default))
            .Should()
            .Contain("\"name\":\"Village\"");
    }

    [Fact]
    public async Task PublishOnSave_PublishesEverySave()
    {
        UseConfig(new WorldConfig { PublishOnSave = true });
        await CreateLockedAsync();

        (await _service.SaveAsync("village", Json(), Author, "", default))
            .Published.Should()
            .BeTrue();
    }

    [Fact]
    public async Task ADocumentForAnotherKey_IsRefused()
    {
        await CreateLockedAsync();

        (
            await _service.SaveAsync(
                "village",
                ZoneJson.Serialize(ZoneSamples.Minimal("forest")),
                Author,
                "",
                default
            )
        )
            .Findings.Should()
            .Contain(f => f.Code == "KeyMismatch");
    }

    [Fact]
    public async Task TheRoomSource_ServesThePublishedDocument_AndNothingForOtherRooms()
    {
        ZoneSummary zone = await _service.CreateAsync("village", "Village", Author, null, default);
        await _service.TryLockAsync("village", Author, false, default);
        ZoneRoomSource source = new(
            new OptionsDbContextFactory(_options),
            NullLogger<ZoneRoomSource>.Instance
        );

        (await source.GetPublishedAsync(new RoomId(zone.RoomId), default)).Should().BeNull();

        ZoneSaveResult saved = await _service.SaveAsync("village", Json(), Author, "", default);
        await _service.PublishAsync("village", saved.Version!.Value, default);

        (await source.GetPublishedAsync(new RoomId(zone.RoomId), default))!
            .Settings.Id.Should()
            .Be("village");
        (await source.GetPublishedAsync(new RoomId(zone.RoomId + 100), default)).Should().BeNull();
    }

    /// <summary>
    /// A database created before the zone tables existed: every room must still open, as an
    /// ordinary room, rather than every entry crashing on the missing table.
    /// </summary>
    [Fact]
    public async Task WithoutTheZoneTables_EveryRoomIsAnOrdinaryRoom()
    {
        await using (VortexDbContext db = new(_options))
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE zone_versions");
            await db.Database.ExecuteSqlRawAsync("DROP TABLE zones");
        }

        ZoneRoomSource source = new(
            new OptionsDbContextFactory(_options),
            NullLogger<ZoneRoomSource>.Instance
        );

        (await source.GetPublishedAsync(new RoomId(1), default)).Should().BeNull();
    }

    /// <summary>
    /// A passage asks where a zone is each time it is taken. A zone's room never changes and a
    /// published zone stays published, so the first answer is remembered; "not published yet" is not.
    /// </summary>
    [Fact]
    public async Task TheRoomOfAPublishedZone_IsLookedUpOnce()
    {
        ZoneSummary zone = await _service.CreateAsync("village", "Village", Author, null, default);
        await _service.TryLockAsync("village", Author, false, default);
        ZoneRoomSource source = new(
            new OptionsDbContextFactory(_options),
            NullLogger<ZoneRoomSource>.Instance
        );

        (await source.FindPublishedRoomAsync("village", default)).Should().BeNull();

        ZoneSaveResult saved = await _service.SaveAsync("village", Json(), Author, "", default);
        await _service.PublishAsync("village", saved.Version!.Value, default);

        (await source.FindPublishedRoomAsync("village", default))
            .Should()
            .Be(new RoomId(zone.RoomId), "an unpublished answer is not remembered");

        await using (VortexDbContext db = new(_options))
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE zones SET zone_key = 'renamed'");
        }

        (await source.FindPublishedRoomAsync("village", default))
            .Should()
            .Be(new RoomId(zone.RoomId), "the database is not asked a second time");
    }

    [Fact]
    public async Task SavingToAnUnknownZone_IsNotFound()
    {
        Func<Task> save = () => _service.SaveAsync("moon", Json(), Author, "", default);

        await save.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task AMissingTemplate_CreatesNothing()
    {
        Func<Task> create = () => _service.CreateAsync("copy", "Copie", Author, "nowhere", default);

        await create.Should().ThrowAsync<KeyNotFoundException>();
        (await _service.ListAsync(default)).Should().BeEmpty();
    }

    /// <summary>
    /// The room, the zone and the template's copy are written together or not at all: a failure
    /// half-way used to leave a room nothing pointed at, found again only by its description.
    /// </summary>
    [Fact]
    public async Task ACreationThatFailsHalfWay_LeavesNoRoomBehind()
    {
        await CreateLockedAsync();
        await _service.SaveAsync("village", Json(), Author, "", default);
        await _service.PublishAsync("village", 1, default);
        _options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseSqlite(_conn, sqlite => sqlite.ExecutionStrategy(d => new RetryingStrategy(d)))
            .AddInterceptors(new FailingZoneVersionInterceptor())
            .Options;
        UseConfig(new WorldConfig());

        Func<Task> create = () => _service.CreateAsync("copy", "Copie", Author, "village", default);

        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("database lost");
        await using VortexDbContext db = new(_options);
        (await db.Rooms.CountAsync()).Should().Be(1, "only the template's own room");
        (await db.Zones.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task MissingReferenceData_SaysWhatToRun()
    {
        await using (VortexDbContext db = new(_options))
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM room_models");
        }

        Func<Task> create = () => _service.CreateAsync("village", "Village", Author, null, default);

        (await create.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*import-reference-data*"
        );
        (await _service.ListAsync(default)).Should().BeEmpty();
    }

    [Fact]
    public async Task ASaveThatLosesTheRaceForItsVersion_IsAConflictFinding()
    {
        await CreateLockedAsync();
        _options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseSqlite(_conn, sqlite => sqlite.ExecutionStrategy(d => new RetryingStrategy(d)))
            .AddInterceptors(new ConflictingVersionInterceptor())
            .Options;
        UseConfig(new WorldConfig());

        ZoneSaveResult result = await _service.SaveAsync("village", Json(), Author, "", default);

        result.Saved.Should().BeFalse();
        result.Findings.Should().Contain(f => f.Code == "Conflict");
    }

    [Fact]
    public async Task ANullTile_IsAFindingNotACrash()
    {
        await CreateLockedAsync();
        string json = Json().Replace("\"tiles\":[{", "\"tiles\":[null,{");

        ZoneSaveResult result = await _service.SaveAsync("village", json, Author, "", default);

        json.Should().Contain("\"tiles\":[null,");
        result.Saved.Should().BeFalse();
        result.Findings.Should().Contain(f => f.Severity == ZoneSeverity.Error);
    }

    private async Task CreateLockedAsync()
    {
        await _service.CreateAsync("village", "Village", Author, null, default);
        await _service.TryLockAsync("village", Author, false, default);
    }

    private static string Json(string name = "Village") =>
        ZoneJson.Serialize(
            ZoneSamples.Minimal() with
            {
                Settings = new ZoneSettings { Id = "village", Name = name },
            }
        );

    private void UseConfig(WorldConfig config)
    {
        IFurnitureDefinitionProvider defs = FakeProxy.Create<IFurnitureDefinitionProvider>(call =>
            call.Method.Name == nameof(IFurnitureDefinitionProvider.TryGetDefinition)
            && (int)call.Args![0]! == ZoneSamples.Throne
                ? TestDefinitions.Floor(ZoneSamples.Throne, "throne", 1, 1)
            : call.Method.Name == nameof(IFurnitureDefinitionProvider.All)
                ? new List<FurnitureDefinitionSnapshot>()
            : null
        );

        _service = new ZoneEditorService(
            new OptionsDbContextFactory(_options),
            new ZoneValidator(defs, Options.Create(config)),
            defs,
            Options.Create(config),
            new ZoneRoomReloader(id =>
            {
                _reloaded.Add(id);
                return Task.CompletedTask;
            }),
            NullLogger<ZoneEditorService>.Instance
        );
    }
}
