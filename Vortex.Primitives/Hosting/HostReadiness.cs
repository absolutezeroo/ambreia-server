namespace Vortex.Primitives.Hosting;

/// <summary>The host's readiness, flipped by the emulator at the end of its start and at the start
/// of its stop. Reads and writes are single words: no lock needed.</summary>
public sealed class HostReadiness : IHostReadiness
{
    private volatile string? _blocker = "starting";

    public bool IsReady => _blocker is null;

    public string? Blocker => _blocker;

    /// <summary>The listeners are open: players can come.</summary>
    public void MarkReady() => _blocker = null;

    /// <summary>The host is draining: a player routed here now would be told to leave.</summary>
    public void MarkStopping() => _blocker = "stopping";
}
