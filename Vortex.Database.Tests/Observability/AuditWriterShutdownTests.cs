using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Tests.Support;
using Vortex.Observability.Audit;
using Vortex.Observability.Configuration;
using Vortex.Primitives.Observability;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Observability;

/// <summary>
/// A stop that lands while a batch is being written cancels that write. The final drain used to
/// clear the batch before reading what was left in the channel, so those records reached neither
/// the database nor the dead-letter file.
/// </summary>
public sealed class AuditWriterShutdownTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task ABatchCancelledMidFlush_IsStillWrittenByTheFinalDrain()
    {
        ObservabilityConfig config = new() { AuditDeadLetterPath = string.Empty };
        AuditChannel channel = new(Options.Create(config));
        StallingFactory factory = new(_options);

        ChannelAuditSink sink = new(
            channel,
            FakeProxy.Create<IVortexContextAccessor>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            NullLogger<ChannelAuditSink>.Instance
        );
        sink.Emit(new AuditEvent { Category = AuditCategory.Auth, Action = "auth.login" });

        using AuditWriterService writer = new(
            channel,
            factory,
            Options.Create(config),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            NullLogger<AuditWriterService>.Instance
        );

        await writer.StartAsync(CancellationToken.None);
        await factory.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await writer.StopAsync(CancellationToken.None);

        await using VortexDbContext db = new(_options);
        (await db.AuditEvents.CountAsync()).Should().Be(1);
    }

    /// <summary>The first write hangs until the stop cancels it; later ones reach SQLite.</summary>
    private sealed class StallingFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        private int _calls;

        public TaskCompletionSource FirstWriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public VortexDbContext CreateDbContext() => new(options);

        public async Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstWriteStarted.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }

            return new VortexDbContext(options);
        }
    }
}
