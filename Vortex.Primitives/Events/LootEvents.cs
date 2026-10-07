namespace Vortex.Primitives.Events;

/// <summary>
/// A prize was drawn from a pool and actually granted. Raised by the grant grain rather than by each
/// trigger, so a new reward furniture cannot ship without its draws being on the audit trail — and so
/// every draw lands in one queryable stream keyed by pool, which is what makes "what this pool really
/// paid out versus what its weights promise" answerable.
/// </summary>
public sealed record LootAwardedEvent(
    int PlayerId,
    string PoolCode,
    int EntryId,
    string Variant,
    string ContentType,
    int ClassId,
    string Source
) : IEvent;
