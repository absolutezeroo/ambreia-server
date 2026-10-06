namespace Vortex.Primitives.Observability;

/// <summary>
/// Small façade over the runtime metric instruments. Kept intentionally narrow; further instruments
/// are added as later phases land. Implementations must be cheap and allocation-free on the hot path.
/// Every recording method takes only bounded dimensions — a player id or a room id must never reach a
/// metric tag; those breakdowns belong to <c>ILiveStatsAggregator</c>.
/// </summary>
public interface IVortexMetrics
{
    /// <summary>
    /// Whether anything is actually recorded. Callers on a hot path (the room tick runs every 50ms
    /// per room) read this first so that a disabled metrics stack costs one boolean test instead of a
    /// timestamp pair per step.
    /// </summary>
    bool Enabled { get; }

    void PacketReceived(string operation, long? actorId = null, int? roomId = null);

    void PacketCompleted(
        string operation,
        double elapsedMilliseconds,
        long? actorId = null,
        int? roomId = null
    );

    void PacketFailed(string operation, long? actorId = null, int? roomId = null);

    void PacketDropped(string reason);

    /// <summary>
    /// One step of a room tick finished. <paramref name="step"/> is the fixed step name declared in
    /// the tick loop ("pets", "wired", ...) — a bounded dimension, so it is safe as a tag.
    /// </summary>
    void RoomTickStepCompleted(string step, double elapsedMilliseconds);

    /// <summary>A whole room tick finished, all steps included. Untagged: the room id is not safe to tag by.</summary>
    void RoomTickCompleted(double elapsedMilliseconds);

    /// <summary>
    /// A value-moving operation changed state. Before this there was no signal at all for the
    /// commerce flows — an operation stuck past its pivot was indistinguishable from one that
    /// completed, for as long as nobody happened to look at the table.
    /// </summary>
    void CommerceOperationTransitioned(
        Vortex.Primitives.Commerce.CommerceOperationKind kind,
        Vortex.Primitives.Commerce.CommerceOperationState state
    );

    /// <summary>
    /// A post-pivot step was asked to run again and found its own receipt. Expected traffic on a
    /// retry; a rising rate means something upstream is failing after doing its work.
    /// </summary>
    void CommerceStepReplayed(string stepKey);

    /// <summary>
    /// A reference-data cache published a new version. Reloads are admin actions, so the useful
    /// question is not how often they happen but which version is being served — an operator who has
    /// just edited definitions wants to know the emulator agrees, and the count of this counter is
    /// that version. <paramref name="version"/> is not a tag: it is monotonic, and tagging by it
    /// would grow a new time series per reload.
    /// </summary>
    void ReferenceDataPublished(string provider, int version);

    /// <summary>
    /// A furniture definition asked for a logic nobody registered and got the family default. The
    /// warning that used to be the only signal is the to-do list for implementing behaviour; a
    /// counter is how a hotel finds out how much of its catalogue is on it.
    /// </summary>
    void FurnitureLogicFallback(string logicName, string family);

    /// <summary>
    /// A call to the (single, global) room directory grain returned. <paramref name="method"/> is the
    /// grain method name, which is bounded by the interface.
    /// </summary>
    void RoomDirectoryCallCompleted(string method, double elapsedMilliseconds);

    /// <summary>
    /// A dashboard login attempt finished. <paramref name="outcome"/> is a
    /// <c>DashboardLoginResult</c> kind — authenticated, invalid-credentials, mfa-required,
    /// invalid-code, forbidden — a closed set, and the one dimension that matters: a rising
    /// invalid-code rate is someone working a stolen password against a second factor, which reads
    /// nothing like a rising invalid-credentials rate.
    /// </summary>
    void DashboardAuthAttempt(string outcome);

    /// <summary>
    /// An authenticated operator was refused an endpoint. <paramref name="capability"/> is the
    /// policy name, which is <c>Capabilities.Dashboard.All</c> and therefore bounded. This is the
    /// difference between "someone cannot find the button" and "someone is walking the API".
    /// </summary>
    void DashboardAuthorizationDenied(string capability);

    /// <summary>
    /// A dashboard write operation finished. <paramref name="action"/> is the audit action name
    /// (a fixed set declared at the call sites) and <paramref name="outcome"/> is success, rejected
    /// or failed — the same three the audit row records, so the metric and the trail agree.
    /// </summary>
    void DashboardOperationCompleted(string action, string outcome, double elapsedMilliseconds);

    /// <summary>A dashboard API request answered with an error status.</summary>
    void DashboardHttpError(int statusCode);

    /// <summary>
    /// An audit event will not reach the table. <paramref name="stage"/> is where it was lost:
    /// <c>enqueue</c> (the channel was saturated and the event was never handed to the writer) or
    /// <c>persist</c> (the writer exhausted its retries and dead-lettered the batch).
    /// <para>
    /// The control-plane note calls an alert on this priority-one, and it is the only counter here
    /// that is not about performance: a privileged mutation that went unaudited is not slow, it is
    /// unaccounted for. Not named <c>dashboard.*</c> on purpose — one pipeline carries the game's
    /// audit too, and a name that claimed otherwise would send an operator looking in one half of it.
    /// </para>
    /// </summary>
    void AuditWriteFailed(string stage);
}
