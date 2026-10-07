using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Hosting;

/// <summary>
/// A cache of reference data (furniture definitions, room models, ...) loaded
/// once at startup and reloadable on demand. Implemented by a provider's existing concrete class
/// alongside its specific interface so <c>VortexEmulator</c> can discover and load every provider
/// through <see cref="System.Collections.Generic.IEnumerable{T}"/> instead of a fixed constructor
/// parameter list.
/// </summary>
public interface IReferenceDataProvider
{
    /// <summary>
    /// Providers in the same stage are reloaded concurrently; stages run in ascending order. Two
    /// providers only need different stages when one's reload genuinely depends on data the other
    /// just loaded — independent reference caches should share a stage.
    /// </summary>
    int LoadStage { get; }

    Task ReloadAsync(CancellationToken ct);
}
