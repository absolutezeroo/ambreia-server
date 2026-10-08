namespace Vortex.Primitives.Observability;

/// <summary>
/// Small façade over the runtime metric instruments. Kept intentionally narrow; further instruments
/// are added as later phases land. Implementations must be cheap and allocation-free on the hot path.
/// Every recording method takes only bounded dimensions — a player id or a room id must never reach a
/// metric tag.
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
    /// the tick loop ("avatars", "flush-tiles", ...) — a bounded dimension, so it is safe as a tag.
    /// </summary>
    void RoomTickStepCompleted(string step, double elapsedMilliseconds);

    /// <summary>A whole room tick finished, all steps included. Untagged: the room id is not safe to tag by.</summary>
    void RoomTickCompleted(double elapsedMilliseconds);

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
    /// counter is how a hotel finds out how many of its definitions are on it.
    /// </summary>
    void FurnitureLogicFallback(string logicName, string family);

    /// <summary>A character grain activated (+1) or deactivated (−1): how many are live (spec
    /// Personnage §13).</summary>
    void CharacterActivated(int delta);

    /// <summary>A character's progress was written; <paramref name="failed"/> when the write threw
    /// and the state stays dirty for the next flush.</summary>
    void CharacterFlushCompleted(double elapsedMilliseconds, bool failed);

    /// <summary>A character-screen packet refused (spec F3a §8): <paramref name="reason"/> is
    /// <c>phase</c> (sent outside the account phase) or <c>rate_limited</c>.</summary>
    void RpgPacketRefused(string reason);

    /// <summary>
    /// A call to the (single, global) room directory grain returned. <paramref name="method"/> is the
    /// grain method name, which is bounded by the interface.
    /// </summary>
    void RoomDirectoryCallCompleted(string method, double elapsedMilliseconds);

    /// <summary>
    /// An audit event will not reach the table. <paramref name="stage"/> is where it was lost:
    /// <c>enqueue</c> (the channel was saturated and the event was never handed to the writer) or
    /// <c>persist</c> (the writer exhausted its retries and dead-lettered the batch).
    /// <para>
    /// The control-plane note calls an alert on this priority-one, and it is the only counter here
    /// that is not about performance: a privileged mutation that went unaudited is not slow, it is
    /// unaccounted for.
    /// </para>
    /// </summary>
    void AuditWriteFailed(string stage);
}
