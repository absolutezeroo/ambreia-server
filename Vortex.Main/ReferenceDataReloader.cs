using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Hosting;

namespace Vortex.Main;

/// <summary>
/// The runtime half of reference-data loading. <see cref="VortexEmulator"/> is the startup half, and
/// they share no code on purpose — see <see cref="IReferenceDataReloader"/> for why.
/// </summary>
/// <remarks>
/// It exists because content written outside an admin service — a SQL script, a restore, another
/// tool — is invisible until the cache that holds it is read again, and until now the only ways
/// were to restart the hotel or to save an unrelated row through the dashboard so its admin service
/// happened to reload. Four domains had each grown their own reload button; this is the one they
/// were each reinventing.
/// </remarks>
/// <param name="extra">
/// Caches that are not <see cref="IReferenceDataProvider"/> singletons because they live inside a
/// grain — fishing definitions, mystery box pools. They were each reachable only through their own
/// console command, which is how the hotel ended up with three words for one idea. Composed in
/// <c>Program.cs</c> rather than known about here, so this class stays a loop over names.
/// </param>
public sealed class ReferenceDataReloader(
    IEnumerable<IReferenceDataProvider> providers,
    ILogger<ReferenceDataReloader> logger,
    IReadOnlyDictionary<string, Func<CancellationToken, Task>>? extra = null
) : IReferenceDataReloader
{
    // Materialised once: the set is fixed for the life of the process, and Providers is read every
    // time an operator asks what there is.
    private readonly ImmutableDictionary<string, Func<CancellationToken, Task>> _byName = Build(
        providers,
        extra,
        logger
    );

    public IReadOnlyList<string> Providers =>
        [.. _byName.Keys.OrderBy(name => name, StringComparer.Ordinal)];

    public async Task<ReloadOutcome> ReloadAsync(string provider, CancellationToken ct)
    {
        // Case-insensitive, and the answer carries the canonical spelling back: the names are type
        // names and nobody types PascalCase reliably.
        string? name = _byName.Keys.FirstOrDefault(k =>
            k.Equals(provider, StringComparison.OrdinalIgnoreCase)
        );

        if (name is null)
        {
            // Not an exception: the name comes from an operator, and a typo is not a fault of the
            // hotel's. It comes back as something to read.
            return new ReloadOutcome(provider, Reloaded: false, ElapsedMs: 0, "No such cache.");
        }

        long started = Stopwatch.GetTimestamp();

        try
        {
            await _byName[name](ct).ConfigureAwait(false);

            long ms = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            logger.LogInformation("Reloaded {Provider} in {ElapsedMs}ms.", name, ms);

            return new ReloadOutcome(name, Reloaded: true, ms);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The runtime policy, and the only place it belongs: the hotel is already up, the cache
            // still holds what it had, and the operator is told rather than the process being taken
            // down for something that was already serving.
            logger.LogError(ex, "Failed to reload {Provider}; it keeps its data.", name);

            return new ReloadOutcome(
                name,
                Reloaded: false,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                ex.Message
            );
        }
    }

    /// <summary>
    /// The name one cache answers to: its type's, with the generic arguments kept.
    /// </summary>
    /// <remarks>
    /// <c>Type.Name</c> alone renders every closed generic as its open form —
    /// <c>CatalogSnapshotProvider&lt;NormalCatalog&gt;</c> and
    /// <c>CatalogSnapshotProvider&lt;BuildersClubCatalog&gt;</c> both come back as
    /// <c>CatalogSnapshotProvider`1</c>. They then collided in the table below and the second
    /// silently replaced the first, which left the normal catalogue — the one every player reads —
    /// reachable by no name at all, while `reload` looked like it had covered it.
    /// </remarks>
    private static string NameOf(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string bare = type.Name[..type.Name.IndexOf('`')];
        string args = string.Join(",", type.GetGenericArguments().Select(a => a.Name));

        return $"{bare}<{args}>";
    }

    /// <summary>
    /// Names every cache, using the provider's type name.
    /// </summary>
    /// <remarks>
    /// Rather than a key property added to twenty-nine classes. The name is already what each one is
    /// called in the logs and in the code, so an operator reading "Failed to reload the
    /// RoomModelProvider" and an operator typing <c>RoomModelProvider</c> are looking at one thing.
    /// </remarks>
    private static ImmutableDictionary<string, Func<CancellationToken, Task>> Build(
        IEnumerable<IReferenceDataProvider> providers,
        IReadOnlyDictionary<string, Func<CancellationToken, Task>>? extra,
        ILogger<ReferenceDataReloader> logger
    )
    {
        ImmutableDictionary<string, Func<CancellationToken, Task>>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, Func<CancellationToken, Task>>(
                StringComparer.Ordinal
            );

        foreach (IReferenceDataProvider provider in providers)
        {
            string name = NameOf(provider.GetType());

            // Two different caches under one name is how the catalogue became unreachable, and
            // nothing said so: the table just kept the last one. Naming both is the only way an
            // operator can tell the list is lying to them.
            if (builder.ContainsKey(name))
            {
                logger.LogError(
                    "Two reference caches answer to the name {Provider}; only the first is "
                        + "reloadable and the rest can only be picked up by a restart. Give the "
                        + "type a distinct name.",
                    name
                );

                continue;
            }

            builder[name] = provider.ReloadAsync;
        }

        foreach (
            KeyValuePair<string, Func<CancellationToken, Task>> entry in extra
                ?? ImmutableDictionary<string, Func<CancellationToken, Task>>.Empty
        )
        {
            builder[entry.Key] = entry.Value;
        }

        return builder.ToImmutable();
    }
}
