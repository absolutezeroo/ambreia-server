using System;
using System.Diagnostics;

namespace Vortex.Primitives.Observability;

/// <summary>
/// Times one outbound call to the room directory grain from the caller's side, so what is measured is
/// the round trip the caller actually waits on — Orleans' queueing and scheduling included — rather
/// than the grain's own execution.
/// </summary>
/// <remarks>
/// A scope instead of a wrapper method on purpose: the caller keeps its own <c>ConfigureAwait</c>. A
/// grain must stay on its scheduler (<c>ConfigureAwait(true)</c>), a plain service must not, and a
/// helper that awaited the call itself would have to pick one and be wrong for half the call sites.
/// </remarks>
public readonly struct RoomDirectoryCallScope : IDisposable, IEquatable<RoomDirectoryCallScope>
{
    private readonly IVortexMetrics? _metrics;
    private readonly string? _method;
    private readonly long _startedAt;

    internal RoomDirectoryCallScope(IVortexMetrics metrics, string method)
    {
        _metrics = metrics;
        _method = method;
        _startedAt = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Records the elapsed time. A default-constructed scope — what
    /// <see cref="VortexMetricsExtensions.MeasureRoomDirectoryCall"/> hands back when metrics are off —
    /// holds no metrics instance and does nothing here.
    /// </summary>
    public void Dispose()
    {
        if (_metrics is not null)
        {
            _metrics.RoomDirectoryCallCompleted(
                _method!,
                Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds
            );
        }
    }

    public bool Equals(RoomDirectoryCallScope other) =>
        ReferenceEquals(_metrics, other._metrics)
        && _method == other._method
        && _startedAt == other._startedAt;

    public override bool Equals(object? obj) =>
        obj is RoomDirectoryCallScope other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_metrics, _method, _startedAt);

    public static bool operator ==(RoomDirectoryCallScope left, RoomDirectoryCallScope right) =>
        left.Equals(right);

    public static bool operator !=(RoomDirectoryCallScope left, RoomDirectoryCallScope right) =>
        !left.Equals(right);
}
