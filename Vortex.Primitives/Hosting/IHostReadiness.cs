namespace Vortex.Primitives.Hosting;

/// <summary>
/// Whether the hotel can take a player right now: the silo is up, the reference data is loaded and
/// the game listeners are open — and the host is not draining. Liveness is a different question
/// (is the process answering?), asked by the orchestrator to decide a restart; this one is asked by
/// the proxy to decide whether to route a player here. An interface because the web API borrows it
/// across the plugin boundary.
/// </summary>
public interface IHostReadiness
{
    bool IsReady { get; }

    /// <summary>Why the hotel is not ready, when it is not: "starting", "stopping".</summary>
    string? Blocker { get; }
}
