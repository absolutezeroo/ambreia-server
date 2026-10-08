namespace Vortex.Primitives.Observability;

public static class VortexMetricsExtensions
{
    /// <summary>
    /// Opens a timing scope around a room-directory call. Wrap the <c>await</c> in a <c>using</c>:
    /// <code>
    /// using (_metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.GetRoomPopulationAsync)))
    /// {
    ///     population = await _grainFactory.GetRoomDirectoryGrain().GetRoomPopulationAsync(roomId);
    /// }
    /// </code>
    /// When metrics are disabled this is a boolean test and a zeroed struct — nothing is timed.
    /// </summary>
    public static RoomDirectoryCallScope MeasureRoomDirectoryCall(
        this IVortexMetrics metrics,
        string method
    ) => metrics.Enabled ? new RoomDirectoryCallScope(metrics, method) : default;
}
