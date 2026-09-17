using System;
using System.Threading.Tasks;

namespace Vortex.Primitives.Plugins.Exports;

public interface IExport<T>
    where T : class
{
    /// <summary>
    /// Whether anything has been published yet. <see cref="Current" /> throws until this is true.
    /// </summary>
    /// <remarks>
    /// The host holds an export from the moment it first asks for one, which is long before — and
    /// often instead of — the plugin that fills it ever being loaded. A host-side consumer therefore
    /// has to be able to ask "is this here?" without an exception as the answer: a plugin that was
    /// never installed is the ordinary case, not a fault.
    /// </remarks>
    bool IsBound { get; }

    T Current { get; }
    IDisposable Subscribe(Action<T> onSwap);
    Task SwapAsync(T instance);
}
