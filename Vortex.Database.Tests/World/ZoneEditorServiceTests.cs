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
        (_conn, _options) = await SqliteTestDb.OpenAsync();

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
        ZoneRoomSource source = new(new Factory(_options), NullLogger<ZoneRoomSource>.Instance);

        (await source.GetPublishedAsync(new RoomId(zone.RoomId), default)).Should().BeNull();

        ZoneSaveResult saved = await _service.SaveAsync("village", Json(), Author, "", default);
        await _service.PublishAsync("village", saved.Version!.Value, default);

        (await source.GetPublishedAsync(new RoomId(zone.RoomId), default))!
            .Settings.Id.Should()
            .Be("village");
        (await source.GetPublishedAsync(new RoomId(zone.RoomId + 100), default)).Should().BeNull();
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
            new Factory(_options),
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

    private sealed class Factory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }
}
