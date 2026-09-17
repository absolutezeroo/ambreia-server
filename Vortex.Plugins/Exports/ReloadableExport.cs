using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Primitives.Plugins.Exports;

namespace Vortex.Plugins.Exports;

public sealed class ReloadableExport<T>(ILogger? logger = null) : IExport<T>, IExportSlot
    where T : class
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private volatile T? _current;
    private ImmutableArray<Action<T>> _subs = [];

    public bool IsBound => _current is not null;

    public T Current =>
        _current ?? throw new InvalidOperationException($"Export {typeof(T).Name} not bound yet.");

    /// <summary>
    /// Publishes <paramref name="value" /> and disposes whatever it replaced. This is the
    /// commit-immediately form; plugin activation goes through
    /// <see cref="IExportSlot.SwapDeferredAsync" /> instead, so the previous instance survives until
    /// the activation is known to have succeeded and can be restored if it has not.
    /// </summary>
    public async Task SwapAsync(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        T? previous = Publish(value);

        await DisposeInstanceAsync(previous, "previous").ConfigureAwait(false);
    }

    // ── IExportSlot ──────────────────────────────────────────────────────────

    Task<object?> IExportSlot.SwapDeferredAsync(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Deliberately does NOT dispose the previous instance. A failed activation has to be able to
        // put it back, and an already-disposed object is not a restoration — it is a second, subtler
        // outage. Disposal is deferred to DisposeSupersededAsync once the activation commits.
        return Task.FromResult<object?>(Publish((T)value));
    }

    async Task IExportSlot.RollbackAsync(object? previous, object failed)
    {
        // Re-publish the old instance (or leave the slot unbound if there never was one) and notify
        // subscribers, so nobody is left holding the half-activated plugin's object.
        if (previous is T restored)
        {
            Publish(restored);
        }
        else
        {
            Interlocked.Exchange(ref _current, null);
        }

        await DisposeInstanceAsync(failed as T, "rolled-back").ConfigureAwait(false);
    }

    async Task IExportSlot.DisposeSupersededAsync(object? instance) =>
        await DisposeInstanceAsync(instance as T, "superseded").ConfigureAwait(false);

    // ── Internals ────────────────────────────────────────────────────────────

    /// <summary>Atomically publishes <paramref name="value" /> and notifies subscribers.</summary>
    private T? Publish(T value)
    {
        T? previous = Interlocked.Exchange(ref _current, value);
        ImmutableArray<Action<T>> subs = _subs;

        foreach (Action<T> s in subs)
        {
            try
            {
                s(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Export swap subscriber failed for {ExportType}",
                    typeof(T).Name
                );
            }
        }

        return previous;
    }

    private async Task DisposeInstanceAsync(T? instance, string role)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            switch (instance)
            {
                case IAsyncDisposable a:
                    await a.DisposeAsync().ConfigureAwait(false);

                    break;
                case IDisposable d:
                    d.Dispose();

                    break;
            }
        }
        catch (Exception ex)
        {
            // Best effort, but never silent: a leaked export instance can hold a socket, a file
            // handle or an ALC alive, and that is exactly the kind of leak nobody finds later.
            _logger.LogError(
                ex,
                "Failed to dispose {Role} export instance for {ExportType}",
                role,
                typeof(T).Name
            );
        }
    }

    public IDisposable Subscribe(Action<T> onSwap)
    {
        ImmutableInterlocked.Update(ref _subs, a => a.Add(onSwap));

        T? current = _current;
        if (current is not null)
        {
            try
            {
                onSwap(current);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Export subscribe callback failed for {ExportType}",
                    typeof(T).Name
                );
            }
        }

        return new Unsub(() => ImmutableInterlocked.Update(ref _subs, a => a.Remove(onSwap)));
    }

    private sealed class Unsub(Action a) : IDisposable
    {
        private readonly Action _a = a;

        public void Dispose() => _a();
    }
}
