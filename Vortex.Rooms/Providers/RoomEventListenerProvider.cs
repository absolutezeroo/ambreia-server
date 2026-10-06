using System;
using System.Collections.Generic;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Runtime;

namespace Vortex.Rooms.Providers;

/// <summary>
/// Holds the room-event listeners contributed by scanned assemblies and builds a fresh set per room:
/// a listener is per-room state, so one instance can never be shared across rooms.
/// </summary>
public sealed class RoomEventListenerProvider(IServiceProvider host) : IRoomEventListenerProvider
{
    private readonly IServiceProvider _host = host;
    private readonly List<RoomEventListenerReg> _listeners = [];

    public IDisposable RegisterListener(
        IServiceProvider sp,
        Func<IServiceProvider, IRoomGrain, IRoomEventListener> factory
    )
    {
        RoomEventListenerReg reg = new(sp, factory);

        _listeners.Add(reg);

        return new ActionDisposable(() => _listeners.Remove(reg));
    }

    public IEnumerable<IRoomEventListener> BuildListenersForRoom(IRoomGrain roomGrain)
    {
        foreach (RoomEventListenerReg reg in _listeners)
        {
            IServiceProvider sp = reg.ServiceProvider;

            if (sp != _host)
            {
                sp = new CompositeServiceProvider(sp, _host);
            }

            yield return reg.Factory(sp, roomGrain);
        }
    }

    private sealed record RoomEventListenerReg(
        IServiceProvider ServiceProvider,
        Func<IServiceProvider, IRoomGrain, IRoomEventListener> Factory
    );
}
