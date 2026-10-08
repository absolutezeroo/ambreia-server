using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Plugins;

namespace Vortex.Plugins;

/// <summary>What is on disk, in the order it can be activated, and who depends on whom.</summary>
public sealed partial class PluginManager
{
    /// <summary>
    /// The plugins on disk in activation order, with the folder of each, and the dependents map
    /// rebuilt from them. A plugin whose dependency is missing or cyclic is reported and left out.
    /// </summary>
    private (
        IReadOnlyList<PluginManifest> Manifests,
        Dictionary<string, string> FolderByKey
    ) Discover()
    {
        List<(PluginManifest manifest, string folder)> discovered = DiscoverPlugins();
        IReadOnlyList<PluginManifest> manifests = SortAndReportUnresolved(discovered);

        RebuildDependents(manifests);

        return (
            manifests,
            discovered.ToDictionary(
                d => d.manifest.Key,
                d => d.folder,
                StringComparer.OrdinalIgnoreCase
            )
        );
    }

    private List<(PluginManifest manifest, string folder)> DiscoverPlugins()
    {
        List<(PluginManifest, string)> list = new(16);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string devPath in _config.DevPluginPaths)
        {
            string dir = Path.GetFullPath(devPath);

            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                PluginManifest manifest = PluginHelpers.ReadManifest(dir);

                if (seen.Add(manifest.Key))
                {
                    list.Add((manifest, dir));
                    _logger.LogDebug("Discovered dev plugin {Key} from {Dir}", manifest.Key, dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read dev plugin manifest in {Dir}", dir);
            }
        }

        if (!Directory.Exists(_config.PluginFolderPath))
        {
            return list;
        }

        foreach (string dir in Directory.EnumerateDirectories(_config.PluginFolderPath))
        {
            try
            {
                PluginManifest manifest = PluginHelpers.ReadManifest(dir);

                if (seen.Add(manifest.Key))
                {
                    list.Add((manifest, dir));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read plugin manifest in {Dir}", dir);
            }
        }

        return list;
    }

    private IReadOnlyList<PluginManifest> SortAndReportUnresolved(
        List<(PluginManifest manifest, string folder)> discovered
    )
    {
        IReadOnlyList<PluginManifest> sorted = PluginHelpers.SortManifests([
            .. discovered.Select(d => d.manifest),
        ]);

        foreach ((PluginManifest m, string folder) in discovered)
        {
            if (!sorted.Contains(m))
            {
                _logger.LogError(
                    "Plugin {Key} in {Folder} is skipped: a dependency is missing or cyclic",
                    m.Key,
                    folder
                );
            }
        }

        return sorted;
    }

    private void RebuildDependents(IEnumerable<PluginManifest> manifests)
    {
        _dependents.Clear();

        foreach (PluginManifest manifest in manifests)
        {
            foreach (PluginDependency dep in manifest.Dependencies)
            {
                _dependents.GetOrAdd(dep.Key, _ => []).Add(manifest.Key);
            }
        }
    }
}
