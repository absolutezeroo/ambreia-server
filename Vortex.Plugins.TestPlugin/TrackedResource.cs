using System;

namespace Vortex.Plugins.TestPlugin;

/// <summary>A singleton whose disposal proves the plugin's service provider was disposed.</summary>
public sealed class TrackedResource : IDisposable
{
    public void Dispose() => FailureSwitch.Trace("resource-dispose");
}
