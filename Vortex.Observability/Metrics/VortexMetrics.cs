using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Vortex.Observability.Configuration;
using Vortex.Observability.Diagnostics;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Metrics;

/// <summary>
/// <c>System.Diagnostics.Metrics</c>-based implementation of <see cref="IVortexMetrics"/>. Instruments
/// live under the shared "Vortex" meter and are tagged only by bounded dimensions (the operation
/// name) to keep cardinality safe for Prometheus/OpenTelemetry exporters. Never tag by user id or
/// room id — high-cardinality breakdowns belong to in-memory aggregators, not metric tags.
/// </summary>
public sealed class VortexMetrics : IVortexMetrics, IDisposable
{
    private readonly bool _enabled;
    private readonly Meter _meter;
    private readonly Counter<long> _packetReceived;
    private readonly Histogram<double> _packetDuration;
    private readonly Counter<long> _packetFailed;
    private readonly Counter<long> _packetDropped;
    private readonly Histogram<double> _roomTickStepDuration;
    private readonly Histogram<double> _roomTickDuration;
    private readonly Histogram<double> _roomDirectoryCallDuration;
    private readonly Counter<long> _furnitureLogicFallback;
    private readonly Counter<long> _referenceDataPublished;
    private readonly Counter<long> _auditWriteFailure;
    private readonly ILiveStatsAggregator _liveStats;

    public bool Enabled => _enabled;

    public VortexMetrics(
        IMeterFactory meterFactory,
        ILiveStatsAggregator liveStats,
        IOptions<ObservabilityConfig> options
    )
    {
        _enabled = options.Value.MetricsEnabled;
        _meter = meterFactory.Create(VortexTelemetry.Name, VortexTelemetry.Version);
        _liveStats = liveStats;

        _packetReceived = _meter.CreateCounter<long>(
            "Vortex.packet.received",
            unit: "{packet}",
            description: "Inbound packets accepted for dispatch."
        );
        _packetDuration = _meter.CreateHistogram<double>(
            "Vortex.packet.duration",
            unit: "ms",
            description: "End-to-end handler dispatch time per packet."
        );
        _packetFailed = _meter.CreateCounter<long>(
            "Vortex.packet.failed",
            unit: "{packet}",
            description: "Packets whose dispatch threw an exception."
        );
        _packetDropped = _meter.CreateCounter<long>(
            "Vortex.packet.dropped",
            unit: "{packet}",
            description: "Outgoing packets that could not be encoded and were never sent."
        );
        _roomTickStepDuration = _meter.CreateHistogram<double>(
            "Vortex.room.tick.step.duration",
            unit: "ms",
            description: "Wall time of one step of a room tick, by step name."
        );
        _roomTickDuration = _meter.CreateHistogram<double>(
            "Vortex.room.tick.duration",
            unit: "ms",
            description: "Wall time of a whole room tick, all steps included."
        );
        _roomDirectoryCallDuration = _meter.CreateHistogram<double>(
            "Vortex.room.directory.call.duration",
            unit: "ms",
            description: "Round-trip time of a call to the room directory grain, by method, "
                + "measured at the call site."
        );
        _referenceDataPublished = _meter.CreateCounter<long>(
            "Vortex.reference.published",
            unit: "{version}",
            description: "Reference-data caches publishing a new version, by provider."
        );
        _furnitureLogicFallback = _meter.CreateCounter<long>(
            "Vortex.furniture.logic.fallback",
            unit: "{object}",
            description: "Room objects built on the family default because their logic name is not "
                + "registered - the size of the unimplemented-behaviour backlog, by name."
        );
        _auditWriteFailure = _meter.CreateCounter<long>(
            "Vortex.audit.write.failure",
            unit: "{event}",
            description: "Audit events that will not reach the table, by the stage that lost them "
                + "(enqueue: channel saturated; persist: dead-lettered after retries)."
        );
    }

    public void PacketReceived(string operation, long? actorId = null, int? roomId = null)
    {
        if (_enabled)
        {
            _packetReceived.Add(1, Tag(operation));
        }

        _liveStats.RecordPacketReceived(operation, actorId, roomId);
    }

    public void PacketCompleted(
        string operation,
        double elapsedMilliseconds,
        long? actorId = null,
        int? roomId = null
    )
    {
        if (_enabled)
        {
            _packetDuration.Record(elapsedMilliseconds, Tag(operation));
        }

        _liveStats.RecordPacketCompleted(actorId, roomId, elapsedMilliseconds);
    }

    public void PacketFailed(string operation, long? actorId = null, int? roomId = null)
    {
        if (_enabled)
        {
            _packetFailed.Add(1, Tag(operation));
        }

        _liveStats.RecordPacketFailed(operation, actorId, roomId);
    }

    public void PacketDropped(string reason)
    {
        if (_enabled)
        {
            _packetDropped.Add(1, new KeyValuePair<string, object?>("reason", reason));
        }
    }

    public void ReferenceDataPublished(string provider, int version)
    {
        if (_enabled)
        {
            // Tagged by provider only. The version is monotonic, so tagging by it would grow a
            // new time series on every reload — the unbounded cardinality this interface's own
            // contract forbids. The count of this counter IS the version, which is the same answer.
            _referenceDataPublished.Add(1, new KeyValuePair<string, object?>("provider", provider));
        }
    }

    public void FurnitureLogicFallback(string logicName, string family)
    {
        if (_enabled)
        {
            _furnitureLogicFallback.Add(
                1,
                new KeyValuePair<string, object?>("logic_name", logicName),
                new KeyValuePair<string, object?>("family", family)
            );
        }
    }

    public void RoomTickStepCompleted(string step, double elapsedMilliseconds)
    {
        if (_enabled)
        {
            _roomTickStepDuration.Record(
                elapsedMilliseconds,
                new KeyValuePair<string, object?>("step", step)
            );
        }
    }

    public void RoomTickCompleted(double elapsedMilliseconds)
    {
        if (_enabled)
        {
            _roomTickDuration.Record(elapsedMilliseconds);
        }
    }

    public void RoomDirectoryCallCompleted(string method, double elapsedMilliseconds)
    {
        if (_enabled)
        {
            _roomDirectoryCallDuration.Record(
                elapsedMilliseconds,
                new KeyValuePair<string, object?>("method", method)
            );
        }
    }

    public void AuditWriteFailed(string stage)
    {
        if (_enabled)
        {
            _auditWriteFailure.Add(1, new KeyValuePair<string, object?>("stage", stage));
        }
    }

    private static KeyValuePair<string, object?> Tag(string operation) =>
        new("operation", operation);

    public void Dispose() => _meter.Dispose();
}
