using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;
using Vortex.Rooms.Wired;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// The global placeholder (addon 2000): its "name\tvalue" configuration, which of the owner's other
/// rooms fill its "from another room" dropdown, and reading a value back out of one of them. On
/// SQLite for the reason <see cref="WiredSharedVariableQueryTests"/> gives.
/// </summary>
public sealed class WiredGlobalPlaceholderTests : IAsyncLifetime
{
    private const int Owner = 1;
    private const int Stranger = 2;
    private const int HereRoom = 10;
    private const int OtherRoom = 11;
    private const int StrangersRoom = 12;
    private const int PlaceholderDefinition = 100;

    private SqliteConnection _conn = null!;

    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        await _conn.OpenAsync();

        _options = new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options;

        await using VortexDbContext db = new(_options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");

        await SeedRoomAsync(db, HereRoom, Owner, "Ici");
        await SeedRoomAsync(db, OtherRoom, Owner, "Mon autre appart");
        await SeedRoomAsync(db, StrangersRoom, Stranger, "Chez un autre");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO furniture_definitions (id, sprite_id, name, logic, created_at, updated_at) "
                + "VALUES ({0}, {0}, {1}, {1}, {2}, {2})",
            PlaceholderDefinition,
            "wf_xtra_global_placeholder",
            DateTime.UtcNow
        );
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public void TheValueIsEverythingAfterTheFirstTab() =>
        WiredAddonGlobalPlaceholder
            .ParseConfiguration(" prize \t10\tcredits")
            .Should()
            .Be(("prize", "10\tcredits"));

    [Fact]
    public async Task OnlyTheOwnersOtherRoomsAreOffered_AndOnlyTheirValuedPlaceholders()
    {
        await using VortexDbContext db = new(_options);

        await SeedBoxAsync(db, 900, OtherRoom, Placeholder(0, "prize\t10 credits"));
        await SeedBoxAsync(db, 901, OtherRoom, Placeholder(1, "copy\tprize"));
        await SeedBoxAsync(db, 902, HereRoom, Placeholder(0, "local\tx"));
        await SeedBoxAsync(db, 903, StrangersRoom, Placeholder(0, "secret\ty"));

        List<WiredSharedPlaceholderSnapshot> offered =
            await WiredGlobalPlaceholderIndex.LoadForOwnerAsync(
                db,
                Owner,
                HereRoom,
                CancellationToken.None
            );

        offered
            .Select(p => (p.RoomId, p.RoomName, p.PlaceholderName))
            .Should()
            .Equal((OtherRoom, "Mon autre appart", "prize"));
    }

    [Fact]
    public async Task AReferenceReadsTheOtherRoomsValue_OrNothingOnceItIsGone()
    {
        await using VortexDbContext db = new(_options);

        await SeedBoxAsync(db, 910, OtherRoom, Placeholder(0, "prize\t10 credits"));

        (
            await WiredGlobalPlaceholderIndex.ResolveAsync(
                db,
                OtherRoom,
                "PRIZE",
                CancellationToken.None
            )
        )
            .Should()
            .Be("10 credits");
        (
            await WiredGlobalPlaceholderIndex.ResolveAsync(
                db,
                OtherRoom,
                "gone",
                CancellationToken.None
            )
        )
            .Should()
            .BeNull();
    }

    private static string Placeholder(int type, string config) =>
        JsonSerializer.Serialize(
            new Dictionary<string, WiredData>
            {
                [ExtraDataSectionType.WIRED] = new()
                {
                    IntParams = [type, 0, 0],
                    StringParam = config,
                },
            }
        );

    private static Task SeedRoomAsync(VortexDbContext db, int id, int ownerId, string name) =>
        db.Database.ExecuteSqlRawAsync(
            "INSERT INTO rooms (id, name, player_id, model_id, last_active, wired_timezone, created_at, updated_at) "
                + "VALUES ({0}, {1}, {2}, 1, {3}, '', {3}, {3})",
            id,
            name,
            ownerId,
            DateTime.UtcNow
        );

    private static Task SeedBoxAsync(VortexDbContext db, int id, int roomId, string extraData) =>
        db.Database.ExecuteSqlRawAsync(
            "INSERT INTO furniture (id, player_id, definition_id, room_id, extra_data, deleted_at, created_at, updated_at) "
                + "VALUES ({0}, {1}, {2}, {3}, {4}, NULL, {5}, {5})",
            id,
            Owner,
            PlaceholderDefinition,
            roomId,
            extraData,
            DateTime.UtcNow
        );
}
