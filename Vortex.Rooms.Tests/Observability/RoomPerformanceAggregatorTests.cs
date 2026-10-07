using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vortex.Observability.Configuration;
using Vortex.Observability.Metrics;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Observability;
using Xunit;

namespace Vortex.Rooms.Tests.Observability;

/// <summary>
/// The aggregator is what lets the dashboard show the same numbers the Prometheus endpoint exports,
/// so these tests drive it the way production does: record through <see cref="VortexMetrics"/> and
/// assert the aggregator saw it, rather than calling the aggregator directly.
/// </summary>
[Collection(MeterCollection.NAME)]
public sealed class RoomPerformanceAggregatorTests
{
    [Fact]
    public async Task ItReadsBackWhatVortexMetricsRecorded()
    {
        (RoomPerformanceAggregator Aggregator, VortexMetrics Metrics) started =
            await CreateStartedAsync();
        using RoomPerformanceAggregator aggregator = started.Aggregator;
        using VortexMetrics metrics = started.Metrics;

        metrics.RoomTickStepCompleted("pets", 8.0);
        metrics.RoomTickStepCompleted("pets", 4.0);
        metrics.RoomTickStepCompleted("wired", 2.0);
        metrics.RoomTickCompleted(15.0);
        metrics.RoomDirectoryCallCompleted("GetActiveRoomsAsync", 3.5);

        RoomPerformanceSnapshot snapshot = aggregator.GetSnapshot();

        snapshot.Tick.Count.Should().Be(1);
        snapshot.Tick.SumMs.Should().Be(15.0);

        snapshot.Steps.Should().HaveCount(2);
        RoomPerformanceSeriesStats pets = snapshot.Steps.Single(s => s.Name == "pets");
        pets.Count.Should().Be(2);
        pets.SumMs.Should().Be(12.0);

        snapshot
            .DirectoryCalls.Single()
            .Should()
            .Match<RoomPerformanceSeriesStats>(d =>
                d.Name == "GetActiveRoomsAsync" && d.Count == 1 && d.SumMs == 3.5
            );
    }

    [Fact]
    public async Task StepsAreOrderedByCostAndCarryTheirShareOfTheTick()
    {
        (RoomPerformanceAggregator Aggregator, VortexMetrics Metrics) started =
            await CreateStartedAsync();
        using RoomPerformanceAggregator aggregator = started.Aggregator;
        using VortexMetrics metrics = started.Metrics;

        metrics.RoomTickStepCompleted("pets", 75.0);
        metrics.RoomTickStepCompleted("wired", 20.0);
        metrics.RoomTickStepCompleted("bots", 5.0);

        RoomPerformanceSnapshot snapshot = aggregator.GetSnapshot();

        // Ordered most expensive first -- that ordering is the point of the table.
        snapshot.Steps.Select(s => s.Name).Should().Equal("pets", "wired", "bots");

        // Share is of total step time, so the three add up to 100.
        snapshot
            .Steps.Single(s => s.Name == "pets")
            .ShareOfTickPercent.Should()
            .BeApproximately(75, 0.001);
        snapshot.Steps.Sum(s => s.ShareOfTickPercent).Should().BeApproximately(100, 0.001);
    }

    [Fact]
    public async Task PercentilesComeFromTheRetainedSamples()
    {
        (RoomPerformanceAggregator Aggregator, VortexMetrics Metrics) started =
            await CreateStartedAsync();
        using RoomPerformanceAggregator aggregator = started.Aggregator;
        using VortexMetrics metrics = started.Metrics;

        for (int i = 1; i <= 100; i++)
        {
            metrics.RoomTickStepCompleted("wired", i);
        }

        RoomPerformanceSeriesStats wired = aggregator.GetSnapshot().Steps.Single();

        wired.Count.Should().Be(100);
        wired.P50Ms.Should().Be(50);
        wired.P95Ms.Should().Be(95);
        wired.P99Ms.Should().Be(99);
    }

    [Fact]
    public async Task WhenMetricsAreDisabled_TheAggregatorNeverSubscribes()
    {
        // Disabled means VortexMetrics records nothing at all, so the dashboard must show an empty
        // window rather than a stale or partial one.
        IMeterFactory factory = MeterFactory();
        using RoomPerformanceAggregator aggregator = new RoomPerformanceAggregator(
            Options.Create(new ObservabilityConfig { MetricsEnabled = false })
        );
        await aggregator.StartAsync(CancellationToken.None);

        using VortexMetrics metrics = new VortexMetrics(
            factory,
            new NoopLiveStats(),
            Options.Create(new ObservabilityConfig { MetricsEnabled = false })
        );

        metrics.RoomTickStepCompleted("pets", 8.0);
        metrics.RoomTickCompleted(15.0);

        RoomPerformanceSnapshot snapshot = aggregator.GetSnapshot();

        snapshot.Steps.Should().BeEmpty();
        snapshot.Tick.Count.Should().Be(0);
    }

    [Fact]
    public async Task ItIgnoresInstrumentsFromOtherMeters()
    {
        (RoomPerformanceAggregator Aggregator, VortexMetrics Metrics) started =
            await CreateStartedAsync();
        using RoomPerformanceAggregator aggregator = started.Aggregator;
        using VortexMetrics metrics = started.Metrics;

        // Same instrument name, different meter: must not be mistaken for ours.
        using Meter foreign = new Meter("SomeoneElse", "1.0.0");
        foreign
            .CreateHistogram<double>(RoomPerformanceAggregator.TickStepInstrument)
            .Record(
                999.0,
                new System.Collections.Generic.KeyValuePair<string, object?>("step", "pets")
            );

        aggregator.GetSnapshot().Steps.Should().BeEmpty();
    }

    private static async Task<(
        RoomPerformanceAggregator Aggregator,
        VortexMetrics Metrics
    )> CreateStartedAsync()
    {
        RoomPerformanceAggregator aggregator = new RoomPerformanceAggregator(
            Options.Create(new ObservabilityConfig { MetricsEnabled = true })
        );

        await aggregator.StartAsync(CancellationToken.None);

        // Created after the listener starts, which is also the production order: the observability
        // module's hosted services come up before any room ticks.
        VortexMetrics metrics = new VortexMetrics(
            MeterFactory(),
            new NoopLiveStats(),
            Options.Create(new ObservabilityConfig { MetricsEnabled = true })
        );

        return (aggregator, metrics);
    }

    private static IMeterFactory MeterFactory() =>
        new ServiceCollection()
            .AddMetrics()
            .BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();

    private sealed class NoopLiveStats : ILiveStatsAggregator
    {
        public void RecordPacketReceived(
            string operation,
            long? actorId = null,
            int? roomId = null
        ) { }

        public void RecordPacketCompleted(
            long? actorId = null,
            int? roomId = null,
            double elapsedMilliseconds = 0
        ) { }

        public void RecordPacketFailed(
            string operation,
            long? actorId = null,
            int? roomId = null
        ) { }

        public Task<LiveStatsSnapshot> GetSnapshotAsync() =>
            Task.FromResult(new LiveStatsSnapshot(0, 0, 0, 0, [], [], [], []));
    }
}
