using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Hosting;

/// <summary>
/// Reloads one reference-data cache while the hotel is running.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <em>not</em> the loop <c>VortexEmulator</c> runs at startup, even though both end up
/// calling <see cref="IReferenceDataProvider.ReloadAsync"/>. The two have opposite error policies:
/// a cache that fails to load at boot must stop the hotel starting, because serving with empty
/// reference data is worse than not serving; a cache that fails to reload at runtime must not, because
/// the hotel is already up and the previous data is still good. One method cannot hold both, so
/// there are two paths and they share nothing but the providers themselves.
/// </para>
/// <para>
/// There is no "reload everything" here for the same reason. Reloading everything is what starting
/// up is, and the way to do it is to restart.
/// </para>
/// <para>
/// The console's <c>reload</c> command is the caller.
/// </para>
/// </remarks>
public interface IReferenceDataReloader
{
    /// <summary>
    /// Every reloadable cache, by name — the provider's type name, which is what the operator sees
    /// and what <see cref="ReloadAsync"/> takes.
    /// </summary>
    IReadOnlyList<string> Providers { get; }

    /// <summary>
    /// Reloads one cache. Never throws for a cause the operator can act on: an unknown name and a
    /// failed load both come back as a <see cref="ReloadOutcome"/> to be shown.
    /// </summary>
    Task<ReloadOutcome> ReloadAsync(string provider, CancellationToken ct);
}
