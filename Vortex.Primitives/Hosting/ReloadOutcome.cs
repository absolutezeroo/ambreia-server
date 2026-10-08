namespace Vortex.Primitives.Hosting;

/// <summary>What happened, in a form an operator can be shown.</summary>
/// <param name="Provider">The name asked for.</param>
/// <param name="Reloaded">Whether the cache now holds fresh data.</param>
/// <param name="ElapsedMs">How long it took — a reload that takes minutes is worth seeing.</param>
/// <param name="Error">
/// Why it failed, or null. The previous data is still in place when this is set: a provider that
/// cannot load keeps what it had, which is the whole reason a runtime failure is survivable.
/// </param>
public sealed record ReloadOutcome(
    string Provider,
    bool Reloaded,
    long ElapsedMs,
    string? Error = null
);
