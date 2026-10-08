using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Vortex.Database.Context;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Rooms.Configuration;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Grains;

/// <summary>
/// A real in-process silo, deployed once and shared by every test in
/// <see cref="VortexClusterCollection"/>.
/// </summary>
/// <remarks>
/// This is the counterpart to <see cref="GrainActivationContext"/>, not a replacement for it: that
/// harness builds a grain with <c>new</c> and is the right tool for "call one method, inspect the
/// state". It cannot see anything the runtime does, and the runtime is where several of this
/// codebase's load-bearing assumptions live — one activation per key, turn-based execution so grain
/// code needs no locks, and arguments crossing a grain call by Orleans serialization rather than by
/// reference. Tests for those belong here, and a silo costs seconds to boot, so it is booted once.
/// </remarks>
public sealed class VortexClusterFixture : IAsyncLifetime
{
    /// <summary>Config key carrying the per-run database name through to the silo.</summary>
    private const string DatabaseNameKey = "VortexTests:InMemoryDatabaseName";

    private TestCluster _cluster = null!;

    public IGrainFactory GrainFactory => _cluster.GrainFactory;

    /// <summary>
    /// Reads and writes the same in-memory store the silo's grains use, so a test can seed rows and
    /// then observe what a grain made of them.
    /// </summary>
    public IDbContextFactory<VortexDbContext> Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // A fresh store per run: the silo is shared, so tests would otherwise inherit each other's
        // rows in an order xUnit does not promise.
        string databaseName = $"vortex-cluster-{Guid.NewGuid()}";

        Db = new InMemoryDbContextFactory(databaseName);

        TestClusterBuilder builder = new(1);

        // TestingHost constructs the configurator itself, so the name is handed over through host
        // configuration rather than a captured field — that keeps working if these silos ever stop
        // sharing this process.
        builder.ConfigureHostConfiguration(cfg =>
            cfg.AddInMemoryCollection(
                new Dictionary<string, string?> { [DatabaseNameKey] = databaseName }
            )
        );
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();

        _cluster = builder.Build();

        await _cluster.DeployAsync().ConfigureAwait(true);
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync().ConfigureAwait(true);

        _cluster.Dispose();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            string databaseName =
                siloBuilder.Configuration[DatabaseNameKey]
                ?? throw new InvalidOperationException(
                    $"'{DatabaseNameKey}' did not reach the silo."
                );

            siloBuilder.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddOptions<RoomConfig>();

                services.AddSingleton<IDbContextFactory<VortexDbContext>>(
                    new InMemoryDbContextFactory(databaseName)
                );

                // Grains publish domain events and time their directory calls on paths under test.
                // Neither is what these tests assert on, and both are wide interfaces, so they get
                // the same stub the hand-constructed grain tests use.
                services.AddSingleton(FakeProxy.Create<IEventPublisher>(_ => null));
                services.AddSingleton(FakeProxy.Create<IVortexMetrics>(_ => null));
            });
        }
    }

    private sealed class InMemoryDbContextFactory(string databaseName)
        : IDbContextFactory<VortexDbContext>
    {
        private readonly DbContextOptions<VortexDbContext> _options =
            new DbContextOptionsBuilder<VortexDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;

        public VortexDbContext CreateDbContext() => new(_options);
    }
}
