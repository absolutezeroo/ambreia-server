using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Tests.Support;

namespace Vortex.Players.Tests.Presence;

/// <summary>
/// What a grain asked the runtime to do with its own lifetime: <c>DelayDeactivation</c> and
/// <c>DeactivateOnIdle</c> reach <c>IGrainRuntime</c>, so this listens to every call the grain
/// resolves and picks the two out by name.
/// </summary>
internal sealed class GrainLifetimeRecorder
{
    public List<TimeSpan> Delays { get; } = [];

    public int Deactivations { get; private set; }

    public object? Observe(ProxyCall call)
    {
        switch (call.Method.Name)
        {
            case "DelayDeactivation":
                Delays.Add(call.Args?.OfType<TimeSpan>().FirstOrDefault() ?? TimeSpan.Zero);
                break;
            case "DeactivateOnIdle":
                Deactivations++;
                break;
        }

        return null;
    }
}
