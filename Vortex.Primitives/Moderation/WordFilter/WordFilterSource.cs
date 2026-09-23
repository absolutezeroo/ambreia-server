namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>
/// Where a filtered text was written — the journal's "source" column. Persisted as its number, so
/// append only; batch 2 of the filter adds the remaining sinks after <see cref="FeedPost"/>.
/// </summary>
public enum WordFilterSource
{
    RoomChat = 0,
    Messenger = 1,
    RoomInvite = 2,
    Motto = 3,
    Username = 4,
    FeedPost = 5,
}
