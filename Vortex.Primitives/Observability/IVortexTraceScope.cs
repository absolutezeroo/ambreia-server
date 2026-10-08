using System;

namespace Vortex.Primitives.Observability;

/// <summary>
/// Lifetime of an active trace context. Disposing restores the previously active context and ends
/// any associated logging scope and activity span.
/// </summary>
public interface IVortexTraceScope : IDisposable
{
    IVortexContext Context { get; }
}
