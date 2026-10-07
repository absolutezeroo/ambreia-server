using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Vortex.Plugins.Exports;

internal sealed class ExportRegistry(ILogger? logger = null)
{
    private readonly ConcurrentDictionary<(Type t, string key), object> _map = new();
    private readonly ILogger? _logger = logger;

    public ReloadableExport<T> GetOrCreate<T>(string exportKey)
        where T : class => (ReloadableExport<T>)GetOrCreate(typeof(T), exportKey);

    private IExportSlot GetOrCreate(Type contract, string exportKey) =>
        (IExportSlot)
            _map.GetOrAdd(
                (contract, exportKey),
                key =>
                    Activator.CreateInstance(
                        typeof(ReloadableExport<>).MakeGenericType(key.t),
                        _logger
                    )!
            );

    /// <summary>Publishes an export and disposes the instance it replaces, immediately.</summary>
    public Task SwapAsync<T>(string exportKey, T instance)
        where T : class => GetOrCreate<T>(exportKey).SwapAsync(instance);

    /// <summary>
    /// Publishes an export as part of a plugin activation, recording the displaced instance in
    /// <paramref name="journal" /> so the bind can be undone if a later activation step fails.
    /// </summary>
    public async Task SwapJournaledAsync(
        Type contract,
        string exportKey,
        object instance,
        ExportJournal journal
    )
    {
        if (!contract.IsInstanceOfType(instance))
        {
            throw new ArgumentException(
                $"Export '{exportKey}' is not a {contract.FullName}.",
                nameof(instance)
            );
        }

        IExportSlot slot = GetOrCreate(contract, exportKey);
        object? previous = await slot.SwapDeferredAsync(instance).ConfigureAwait(false);

        journal.Record(slot, exportKey, previous, instance);
    }
}
