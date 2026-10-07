using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Plugins.Configuration;

namespace Vortex.Plugins;

public sealed class PluginHotReloadService(
    PluginManager pluginManager,
    IOptions<PluginConfig> config,
    ILogger<PluginHotReloadService> logger
) : IHostedService, IDisposable
{
    private static readonly string[] WATCH_GLOBS =
    [
        "manifest.json",
        "*.dll",
        "*.pdb",
        "*.deps.json",
    ];

    private readonly PluginConfig _config = config.Value;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<PluginHotReloadService> _logger = logger;

    private readonly PluginManager _pluginManager = pluginManager;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly List<FileSystemWatcher> _watchers = [];

    private Timer? _debounceTimer;
    private string? _lastPath;
    private bool _pendingReload;
    private int _shutdownStarted;

    // The gate and the token source are left to the GC: a reload queued by the debounce timer can
    // still be starting when the host disposes this, and it reads both. Neither holds a handle.
    public void Dispose() => Shutdown();

    public Task StartAsync(CancellationToken ct)
    {
        _debounceTimer = new Timer(
            _ => _ = Task.Run(ProcessReloadAsync, _cts.Token),
            null,
            Timeout.Infinite,
            Timeout.Infinite
        );

        WatchDirectory(_config.PluginFolderPath, true);

        foreach (string devPath in _config.DevPluginPaths)
        {
            WatchDirectory(Path.GetFullPath(devPath), false);
        }

        _logger.LogInformation("Plugin hot reload is enabled.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        Shutdown();
        return Task.CompletedTask;
    }

    private void WatchDirectory(string path, bool includeSubdirectories)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        FileSystemWatcher watcher = new(path)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter =
                NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.CreationTime
                | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        watcher.Changed += OnFsEvent;
        watcher.Created += OnFsEvent;
        watcher.Deleted += OnFsEvent;
        watcher.Renamed += OnFsRenamed;
        watcher.Error += OnFsError;

        _watchers.Add(watcher);

        _logger.LogDebug("Watching plugin path: {Path}", path);
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (Volatile.Read(ref _shutdownStarted) == 1)
        {
            return;
        }

        if (!ShouldReactToPath(e.FullPath))
        {
            return;
        }

        QueueDebouncedReload(e.FullPath, e.ChangeType.ToString());
    }

    private void OnFsRenamed(object sender, RenamedEventArgs e)
    {
        if (Volatile.Read(ref _shutdownStarted) == 1)
        {
            return;
        }

        if (!ShouldReactToPath(e.FullPath) && !ShouldReactToPath(e.OldFullPath))
        {
            return;
        }

        QueueDebouncedReload(e.FullPath, "Renamed");
    }

    private void OnFsError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "Plugin file watcher emitted an error event.");
    }

    private void QueueDebouncedReload(string fullPath, string changeType)
    {
        lock (_stateLock)
        {
            _pendingReload = true;
            _lastPath = fullPath;
            _debounceTimer?.Change(Math.Max(100, _config.DebounceMs), Timeout.Infinite);
        }

        _logger.LogDebug(
            "Plugin change detected ({Type}) at {Path}; reload scheduled.",
            changeType,
            fullPath
        );
    }

    private async Task ProcessReloadAsync()
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        string? changedPath;

        lock (_stateLock)
        {
            if (!_pendingReload)
            {
                return;
            }

            _pendingReload = false;
            changedPath = _lastPath;
        }

        bool acquired = false;

        try
        {
            await _reloadGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            acquired = true;

            _logger.LogInformation(
                "Plugin reload started (trigger: {Path})",
                changedPath ?? "<unknown>"
            );

            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await _pluginManager.LoadAllAsync(true, _cts.Token).ConfigureAwait(false);
                    _logger.LogInformation("Plugin reload completed.");
                    return;
                }
                catch (Exception ex)
                    when (attempt < maxAttempts && ex is IOException or InvalidDataException)
                {
                    _logger.LogWarning(
                        ex,
                        "Plugin reload attempt {Attempt}/{MaxAttempts} failed due to file churn; retrying.",
                        attempt,
                        maxAttempts
                    );

                    await Task.Delay(250, _cts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // host is stopping
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin reload failed.");
        }
        finally
        {
            // A wait cancelled at shutdown never took the gate; releasing it anyway throws.
            if (acquired)
            {
                _reloadGate.Release();
            }
        }
    }

    private static bool ShouldReactToPath(string path)
    {
        string fileName = Path.GetFileName(path);

        return !string.IsNullOrWhiteSpace(fileName)
            && WATCH_GLOBS.Any(glob => FileSystemName.MatchesSimpleExpression(glob, fileName));
    }

    private void DisposeWatchers()
    {
        foreach (FileSystemWatcher w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Changed -= OnFsEvent;
            w.Created -= OnFsEvent;
            w.Deleted -= OnFsEvent;
            w.Renamed -= OnFsRenamed;
            w.Error -= OnFsError;
            w.Dispose();
        }

        _watchers.Clear();
    }

    private void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 1)
        {
            return;
        }

        DisposeWatchers();
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _cts.Cancel();
    }
}
