using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Audit;
using Vortex.Observability.Configuration;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Observability;
using Xunit;

namespace Vortex.Database.Tests.Observability;

/// <summary>
/// The sweep decides what is deleted from the tables the moderation tooling reads live, so the two
/// things worth pinning are the cutoff (nothing inside the window is ever touched) and the opt-out
/// (a retention of 0 means keep, not delete everything).
/// </summary>
public sealed class ForensicsRetentionServiceTests : IAsyncLifetime
{
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
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task TheSweep_RemovesOnlyWhatIsOlderThanTheWindow()
    {
        await SeedAuditAsync(ageInDays: 200);
        await SeedAuditAsync(ageInDays: 10);

        await RunSweepAsync(new ObservabilityConfig { AuditRetentionDays = 180 });

        await using VortexDbContext db = new(_options);
        AuditEventEntity[] left = await db.AuditEvents.AsNoTracking().ToArrayAsync();

        left.Should().ContainSingle("the row inside the window is still evidence");
        (DateTime.UtcNow - left[0].OccurredAt).TotalDays.Should().BeLessThan(180);
    }

    [Fact]
    public async Task ARetentionOfZero_KeepsEverything()
    {
        await SeedAuditAsync(ageInDays: 5_000);

        await RunSweepAsync(new ObservabilityConfig { AuditRetentionDays = 0 });

        await using VortexDbContext db = new(_options);

        (await db.AuditEvents.CountAsync())
            .Should()
            .Be(1, "0 means keep for ever; deleting on 0 would silently erase the whole trail");
    }

    /// <summary>
    /// Seeded in raw SQL for the reason the marketplace tests next door spell out: created_at is
    /// DatabaseGenerated(Identity) and updated_at Computed, so EF never writes either, and
    /// EnsureCreated on SQLite makes both NOT NULL with no default. Deletes are unaffected, which is
    /// all the sweep does.
    /// </summary>
    private async Task SeedAuditAsync(int ageInDays)
    {
        await using VortexDbContext db = new(_options);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO audit_events
              (category, action, severity, result, occurred_at, created_at, updated_at)
            VALUES
              ('Room', 'room.entered', 'Info', 'Success',
               datetime('now', {0}), datetime('now'), datetime('now'))
            """,
            $"-{ageInDays} days"
        );
    }

    /// <summary>
    /// Drives one sweep and stops. ExecuteAsync waits five minutes before its first pass, so the
    /// service is started and then cancelled; the sweep itself is invoked directly.
    /// </summary>
    private async Task RunSweepAsync(ObservabilityConfig config)
    {
        using ForensicsRetentionService service = new(
            new TestDbContextFactory(_options),
            Options.Create(config),
            NullLogger<ForensicsRetentionService>.Instance
        );

        await service.SweepOnceAsync(CancellationToken.None);
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
