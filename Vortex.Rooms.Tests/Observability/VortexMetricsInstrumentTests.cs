using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vortex.Observability.Configuration;
using Vortex.Observability.Diagnostics;
using Vortex.Observability.Metrics;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Players;
using Xunit;

namespace Vortex.Rooms.Tests.Observability;

/// <summary>
/// Covers the instruments added for room contention and connection state: that they report, that the
/// only tags they carry are the bounded ones (a room id in a tag would blow up the exporter's
/// cardinality), and that <see cref="ObservabilityConfig.MetricsEnabled"/> genuinely silences them.
/// </summary>
[Collection(MeterCollection.NAME)]
public sealed class VortexMetricsInstrumentTests
{
    [Fact]
    public void RoomTickStepCompleted_RecordsUnderTheStepName()
    {
        using MeasurementRecorder recorder = new MeasurementRecorder();
        using VortexMetrics metrics = CreateMetrics(recorder.Factory, enabled: true);

        metrics.RoomTickStepCompleted("wired", 12.5);
        metrics.RoomTickStepCompleted("pets", 3.0);

        recorder.Collect();

        recorder
            .For("Vortex.room.tick.step.duration")
            .Should()
            .BeEquivalentTo(
                new[] { (12.5, "step", (object?)"wired"), (3.0, "step", (object?)"pets") }
            );
    }

    [Fact]
    public void RoomTickCompleted_RecordsWithoutAnyTag()
    {
        using MeasurementRecorder recorder = new MeasurementRecorder();
        using VortexMetrics metrics = CreateMetrics(recorder.Factory, enabled: true);

        metrics.RoomTickCompleted(41.0);

        recorder.Collect();

        // Untagged on purpose: the only dimension a tick could carry is the room id, and that is
        // exactly what must never reach a metric tag.
        recorder.TagsFor("Vortex.room.tick.duration").Should().BeEmpty();
        recorder.ValuesFor("Vortex.room.tick.duration").Should().Equal(41.0);
    }

    [Fact]
    public void RoomDirectoryCallCompleted_RecordsUnderTheMethodName()
    {
        using MeasurementRecorder recorder = new MeasurementRecorder();
        using VortexMetrics metrics = CreateMetrics(recorder.Factory, enabled: true);

        metrics.RoomDirectoryCallCompleted("GetActiveRoomsAsync", 7.5);

        recorder.Collect();

        recorder
            .For("Vortex.room.directory.call.duration")
            .Should()
            .BeEquivalentTo(new[] { (7.5, "method", (object?)"GetActiveRoomsAsync") });
    }

    [Fact]
    public void WhenMetricsAreDisabled_NothingIsRecordedAndEnabledIsFalse()
    {
        using MeasurementRecorder recorder = new MeasurementRecorder();
        using VortexMetrics metrics = CreateMetrics(recorder.Factory, enabled: false);

        metrics.Enabled.Should().BeFalse();

        metrics.RoomTickStepCompleted("wired", 12.5);
        metrics.RoomTickCompleted(41.0);
        metrics.RoomDirectoryCallCompleted("GetActiveRoomsAsync", 7.5);

        recorder.Collect();

        recorder.ValuesFor("Vortex.room.tick.step.duration").Should().BeEmpty();
        recorder.ValuesFor("Vortex.room.tick.duration").Should().BeEmpty();
        recorder.ValuesFor("Vortex.room.directory.call.duration").Should().BeEmpty();
    }

    [Fact]
    public void MeasureRoomDirectoryCall_WhenDisabled_ProducesAScopeThatRecordsNothing()
    {
        using MeasurementRecorder recorder = new MeasurementRecorder();
        using VortexMetrics metrics = CreateMetrics(recorder.Factory, enabled: false);

        using (metrics.MeasureRoomDirectoryCall("GetActiveRoomsAsync")) { }

        recorder.Collect();

        recorder.ValuesFor("Vortex.room.directory.call.duration").Should().BeEmpty();
    }

    [Fact]
    public void MeasureRoomDirectoryCall_WhenEnabled_RecordsOnDispose()
    {
        using MeasurementRecorder recorder = new MeasurementRecorder();
        using VortexMetrics metrics = CreateMetrics(recorder.Factory, enabled: true);

        using (metrics.MeasureRoomDirectoryCall("UpsertActiveRoomAsync"))
        {
            recorder.Collect();
            recorder
                .ValuesFor("Vortex.room.directory.call.duration")
                .Should()
                .BeEmpty("nothing is recorded until the scope closes");
        }

        recorder.Collect();

        recorder
            .TagsFor("Vortex.room.directory.call.duration")
            .Should()
            .Equal(("method", (object?)"UpsertActiveRoomAsync"));
    }

    [Fact]
    public void ConnectionGauges_ReportTheSessionGatewaysLiveCounts()
    {
        FakeSessionGateway sessions = new FakeSessionGateway(activeSessions: 7, onlinePlayers: 4);

        using MeasurementRecorder recorder = new MeasurementRecorder();
        using ConnectionMetrics connections = new ConnectionMetrics(
            recorder.Factory,
            sessions,
            Options.Create(new ObservabilityConfig { MetricsEnabled = true })
        );

        recorder.Collect();

        recorder.ValuesFor("Vortex.sessions.active").Should().Equal(7d);
        recorder.ValuesFor("Vortex.players.online").Should().Equal(4d);

        // Observable: a second scrape re-reads the gateway rather than replaying a cached value.
        sessions.SetCounts(activeSessions: 9, onlinePlayers: 9);
        recorder.Reset();
        recorder.Collect();

        recorder.ValuesFor("Vortex.sessions.active").Should().Equal(9d);
        recorder.ValuesFor("Vortex.players.online").Should().Equal(9d);
    }

    [Fact]
    public void ConnectionGauges_WhenMetricsAreDisabled_AreNeverCreated()
    {
        using MeasurementRecorder recorder = new MeasurementRecorder();
        using ConnectionMetrics connections = new ConnectionMetrics(
            recorder.Factory,
            new FakeSessionGateway(activeSessions: 7, onlinePlayers: 4),
            Options.Create(new ObservabilityConfig { MetricsEnabled = false })
        );

        recorder.Collect();

        recorder.ValuesFor("Vortex.sessions.active").Should().BeEmpty();
        recorder.ValuesFor("Vortex.players.online").Should().BeEmpty();
    }

    private static VortexMetrics CreateMetrics(IMeterFactory factory, bool enabled) =>
        new VortexMetrics(
            factory,
            Options.Create(new ObservabilityConfig { MetricsEnabled = enabled })
        );

    private static IMeterFactory MeterFactory() =>
        new ServiceCollection()
            .AddMetrics()
            .BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();

    /// <summary>
    /// Listens to the "Vortex" meter and keeps every measurement it sees, so a test can assert on the
    /// values and — just as importantly — on the tags that were attached to them.
    /// </summary>
    private sealed class MeasurementRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(
            string Instrument,
            double Value,
            KeyValuePair<string, object?>[] Tags
        )> _seen = [];

        /// <summary>
        /// The factory whose meter this recorder listens to. Tests must build the metrics under test
        /// from it, so that the instruments they create are the ones observed.
        /// </summary>
        public IMeterFactory Factory { get; } = MeterFactory();

        private readonly Meter _meter;

        public MeasurementRecorder()
        {
            // Filtering by meter *name* listened to the whole process: every test class builds its
            // own factory but they all name the meter the same, so a room test recording a directory
            // call landed in whatever metrics test happened to be running beside it. xunit runs
            // classes in parallel, so which test failed moved around -- classic phantom flake.
            // Each factory caches one meter per name, so the instance is the isolation that was
            // already there and simply was not used.
            // Name *and* version: a factory caches one meter per full identity, so asking for the
            // name alone hands back a different instance than the one the metrics under test use.
            _meter = Factory.Create(VortexTelemetry.Name, VortexTelemetry.Version);

            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, _meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) => Record(instrument, value, tags)
            );
            _listener.SetMeasurementEventCallback<int>(
                (instrument, value, tags, _) => Record(instrument, value, tags)
            );
            _listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) => Record(instrument, value, tags)
            );

            _listener.Start();
        }

        /// <summary>Pulls the observable instruments; recorded histograms arrive on their own.</summary>
        public void Collect() => _listener.RecordObservableInstruments();

        public void Reset() => _seen.Clear();

        public IReadOnlyList<double> ValuesFor(string instrument) =>
            [.. _seen.Where(m => m.Instrument == instrument).Select(m => m.Value)];

        public IReadOnlyList<(string Key, object? Value)> TagsFor(string instrument) =>
            [
                .. _seen
                    .Where(m => m.Instrument == instrument)
                    .SelectMany(m => m.Tags)
                    .Select(t => (t.Key, t.Value)),
            ];

        public IReadOnlyList<(double Value, string TagKey, object? TagValue)> For(
            string instrument
        ) =>
            [
                .. _seen
                    .Where(m => m.Instrument == instrument)
                    .Select(m => (m.Value, m.Tags[0].Key, m.Tags[0].Value)),
            ];

        private void Record(
            Instrument instrument,
            double value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags
        ) => _seen.Add((instrument.Name, value, tags.ToArray()));

        public void Dispose() => _listener.Dispose();
    }

    private sealed class FakeSessionGateway(int activeSessions, int onlinePlayers) : ISessionGateway
    {
        private int _activeSessions = activeSessions;
        private PlayerId[] _online =
        [
            .. Enumerable.Range(1, onlinePlayers).Select(i => (PlayerId)i),
        ];

        public void SetCounts(int activeSessions, int onlinePlayers)
        {
            _activeSessions = activeSessions;
            _online = [.. Enumerable.Range(1, onlinePlayers).Select(i => (PlayerId)i)];
        }

        public int GetActiveSessionCount() => _activeSessions;

        public bool IsOnline(PlayerId playerId) => _online.Contains(playerId);

        public int GetOnlinePlayerCount() => _online.Length;

        public IReadOnlyCollection<PlayerId> GetOnlinePlayerIds() => _online;

        public ISessionContext? GetSession(SessionKey key) => null;

        public ISessionContextObserver? GetSessionObserver(SessionKey key) => null;

        public PlayerId GetPlayerId(SessionKey key) => -1;

        public Task<bool> AddSessionAsync(SessionKey key, ISessionContext ctx) =>
            Task.FromResult(true);

        public Task RemoveSessionAsync(SessionKey key, System.Threading.CancellationToken ct) =>
            Task.CompletedTask;

        public Task AddSessionToPlayerAsync(
            SessionKey key,
            PlayerId playerId,
            System.Threading.CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task RemoveSessionFromPlayerAsync(
            PlayerId playerId,
            System.Threading.CancellationToken ct
        ) => Task.CompletedTask;
    }
}
