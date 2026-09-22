using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Channels;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// Publish/subscribe for the feed itself: which thread moved, to everybody reading.
/// </summary>
/// <remarks>
/// <para>
/// The public twin of <see cref="NotificationBroadcaster"/>, and deliberately not the same class
/// with a nullable key. A notification has exactly one reader and is filtered by player; a feed
/// change has every reader and is filtered by nobody. Folding them together would mean one
/// dictionary whose key means "this player" on some rows and "everyone" on others, which is the
/// kind of sharing that reads as economy and costs a bug.
/// </para>
/// <para>
/// In-process, with the same ceiling and the same upgrade path written out at
/// <see cref="NotificationBroadcaster"/>.
/// </para>
/// <para>
/// Each subscriber's channel holds a handful of ids rather than one value. Unlike an unread
/// count, where only the newest matters, two different threads moving are two different cards to
/// refresh — dropping one would leave it stale until the reader happened to change filter. The
/// bound is small and drops the OLDEST when a tab stops reading: a reader that far behind is
/// better served by the page refetch its next event triggers anyway.
/// </para>
/// </remarks>
public sealed class FeedBroadcaster
{
    /// <summary>
    /// How many pending changes one tab may be behind before the oldest is dropped. Sized for a
    /// burst of activity, not for a tab left open overnight.
    /// </summary>
    private const int Backlog = 32;

    private readonly ConcurrentDictionary<Guid, Channel<int>> _subscribers = new();

    /// <summary>
    /// Opens a subscription for one connection. Disposing the handle closes it, and nothing else
    /// does.
    /// </summary>
    public Subscription Subscribe()
    {
        Channel<int> channel = Channel.CreateBounded<int>(
            new BoundedChannelOptions(Backlog)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        Guid id = Guid.NewGuid();

        _subscribers[id] = channel;

        return new Subscription(this, id, channel.Reader);
    }

    /// <summary>
    /// Tells every open feed that <paramref name="rootPostId"/> changed.
    /// </summary>
    /// <remarks>
    /// Never blocks and never throws: it is called from the feed's write paths, and a reader that
    /// cannot be told must not be able to fail somebody's post.
    /// </remarks>
    public void Publish(int rootPostId)
    {
        foreach (KeyValuePair<Guid, Channel<int>> tab in _subscribers)
        {
            tab.Value.Writer.TryWrite(rootPostId);
        }
    }

    private void Unsubscribe(Guid id) => _subscribers.TryRemove(id, out _);

    /// <summary>One connection's end of the channel. Disposing it is what unsubscribes.</summary>
    public sealed class Subscription(FeedBroadcaster owner, Guid id, ChannelReader<int> reader)
        : IDisposable
    {
        public ChannelReader<int> Reader { get; } = reader;

        public void Dispose() => owner.Unsubscribe(id);
    }
}
