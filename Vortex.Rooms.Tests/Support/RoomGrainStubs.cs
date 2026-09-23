using System;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Tests.Support;

namespace Vortex.Rooms.Tests.Support;

/// <summary>
/// The two <see cref="Vortex.Rooms.Grains.RoomGrain" /> dependencies every test has to pass and no
/// test cares about. Both have a default that must not be the proxy's: an empty listener sequence
/// rather than null, and a context that says "not cancelled" rather than null.
/// </summary>
internal static class RoomGrainStubs
{
    public static IRoomEventListenerProvider NoListeners() =>
        FakeProxy.Create<IRoomEventListenerProvider>(_ => Array.Empty<IRoomEventListener>());

    public static ICancellableEventPublisher NeverCancels() =>
        FakeProxy.Create<ICancellableEventPublisher>(_ =>
            Task.FromResult(new EventContext { CorrelationId = string.Empty })
        );

    /// <summary>
    /// A hotel word filter with an empty list: every text comes back untouched. Not the proxy's
    /// default either — that would answer a zeroed result whose text is null.
    /// </summary>
    public static IWordFilter PassThroughWordFilter() =>
        FakeProxy.Create<IWordFilter>(call =>
            call.Method.Name switch
            {
                nameof(IWordFilter.Apply) or nameof(IWordFilter.Test) => new WordFilterResult(
                    (string)call.Args![0]!,
                    false,
                    false
                ),
                "get_RefusedMessage" => WordFilterConfig.RefusedMessageDefault,
                _ => null,
            }
        );
}
