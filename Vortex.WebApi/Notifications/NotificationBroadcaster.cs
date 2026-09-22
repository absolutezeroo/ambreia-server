using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// Publish/subscribe for notifications: the sink publishes, the open browser tabs listen.
/// </summary>
/// <remarks>
/// <para>
/// In-process, and that is the ceiling. A subscriber here is an HTTP connection held by THIS
/// listener, so a hotel running two hosts behind a load balancer would write a notification on one
/// and hold the connection on the other, and nothing would arrive. That is not this hotel —
/// <c>Vortex.Main</c> carries the silo and the web API in one process — and the upgrade is already
/// running beside it: Orleans memory streams (<c>OrleansStreamProviders</c>), which are
/// cluster-wide and which <c>RoomGrain</c> and <c>PlayerPresenceGrain</c> already use. Swapping to
/// them means replacing this class and nothing above it.
/// </para>
/// <para>
/// Not chosen over Orleans by accident: the streams need an <c>IClusterClient</c> in the web API's
/// container, which the test host has no silo to provide, and an untestable delivery path for a
/// hotel that has one host is the wrong trade today.
/// </para>
/// <para>
/// Each subscriber gets its OWN channel, bounded at one and dropping what it has not read. The
/// payload is a count, not a queue: a tab that was asleep through ten notifications wants the
/// number, not ten copies of the same message. Bounded so a tab that stops reading — a laptop lid
/// closing — costs one integer rather than an unbounded backlog.
/// </para>
/// </remarks>
public sealed class NotificationBroadcaster
{
    /// <summary>
    /// The live subscribers per player. A dictionary of dictionaries rather than a list, because
    /// the common unsubscribe is "this one connection of this one player" and a list would be a
    /// scan under a lock.
    /// </summary>
    private readonly ConcurrentDictionary<
        int,
        ConcurrentDictionary<Guid, Channel<int>>
    > _subscribers = new();

    /// <summary>
    /// Opens a subscription for one connection. Disposing the returned handle closes it, and
    /// nothing else does — a caller that forgets leaks one channel per abandoned connection.
    /// </summary>
    public Subscription Subscribe(int playerId)
    {
        // DropOldest and a capacity of one: the newest unread count is the only one worth having,
        // and an older one is not information the reader lost.
        Channel<int> channel = Channel.CreateBounded<int>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        Guid id = Guid.NewGuid();

        _subscribers.GetOrAdd(playerId, _ => new ConcurrentDictionary<Guid, Channel<int>>())[id] =
            channel;

        return new Subscription(this, playerId, id, channel.Reader);
    }

    /// <summary>
    /// Tells every tab this player has open that their unread count is now
    /// <paramref name="unread"/>.
    /// </summary>
    /// <remarks>
    /// Never blocks and never throws. It is called from the notification sink, which is itself
    /// inside an event handler for something that has already happened — a push that cannot be
    /// delivered must not be able to fail a club renewal.
    /// </remarks>
    public void Publish(int playerId, int unread)
    {
        if (!_subscribers.TryGetValue(playerId, out ConcurrentDictionary<Guid, Channel<int>>? tabs))
        {
            return;
        }

        foreach (KeyValuePair<Guid, Channel<int>> tab in tabs)
        {
            // The bounded channel drops the previous value rather than refusing, so this only
            // fails on a channel already completed -- a connection closing as this runs.
            tab.Value.Writer.TryWrite(unread);
        }
    }

    private void Unsubscribe(int playerId, Guid id)
    {
        if (!_subscribers.TryGetValue(playerId, out ConcurrentDictionary<Guid, Channel<int>>? tabs))
        {
            return;
        }

        tabs.TryRemove(id, out _);

        // The player's own entry goes when their last tab does, or the dictionary grows by one
        // empty bucket per player who ever connected and never shrinks.
        if (tabs.IsEmpty)
        {
            _subscribers.TryRemove(playerId, out _);
        }
    }

    /// <summary>One connection's end of the channel. Disposing it is what unsubscribes.</summary>
    public sealed class Subscription(
        NotificationBroadcaster owner,
        int playerId,
        Guid id,
        ChannelReader<int> reader
    ) : IDisposable
    {
        public ChannelReader<int> Reader { get; } = reader;

        public void Dispose() => owner.Unsubscribe(playerId, id);
    }
}
