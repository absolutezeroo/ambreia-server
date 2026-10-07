using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vortex.Primitives.Plugins;

namespace Vortex.Plugins;

internal static class PluginHelpers
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static PluginManifest ReadManifest(string dir)
    {
        string path = Path.Combine(dir, "manifest.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Plugin manifest not found: {path}");
        }

        try
        {
            PluginManifest manifest =
                (
                    JsonSerializer.Deserialize<PluginManifest>(
                        File.ReadAllText(path),
                        _jsonSerializerOptions
                    ) ?? throw new InvalidOperationException("Invalid manifest.json")
                )
                ?? throw new InvalidDataException($"manifest.json at {path} deserialized to null");

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new InvalidDataException(
                    $"Plugin manifest missing required 'Name' in {path}"
                );
            }

            if (string.IsNullOrWhiteSpace(manifest.Version))
            {
                throw new InvalidDataException(
                    $"Plugin manifest missing required 'Version' in {path}"
                );
            }

            if (string.IsNullOrWhiteSpace(manifest.AssemblyFile))
            {
                throw new InvalidDataException(
                    $"Plugin manifest missing required 'AssemblyFile' in {path}"
                );
            }

            return manifest;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Failed to parse manifest.json for plugin at {dir}: {ex.Message}",
                ex
            );
        }
    }

    /// <summary>
    /// Orders manifests so every plugin comes after its dependencies. A manifest whose dependency is
    /// missing, or that sits on a cycle, is left out -- and so is everything depending on it -- rather
    /// than failing the whole load: one bad folder must not keep every other plugin down.
    /// </summary>
    public static IReadOnlyList<PluginManifest> SortManifests(
        IReadOnlyList<PluginManifest> manifests
    )
    {
        Dictionary<string, PluginManifest> byKey = manifests.ToDictionary(
            m => m.Key,
            StringComparer.OrdinalIgnoreCase
        );
        Dictionary<string, int> indeg = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> graph = new(StringComparer.OrdinalIgnoreCase);

        foreach (PluginManifest m in manifests)
        {
            indeg[m.Key] = 0;
            graph[m.Key] = [];
        }

        foreach (PluginManifest m in manifests)
        {
            foreach (PluginDependency d in m.Dependencies)
            {
                // A missing dependency still counts as an unmet edge, so the manifest never
                // reaches in-degree 0 and is left out with its dependents.
                if (byKey.ContainsKey(d.Key))
                {
                    graph[d.Key].Add(m.Key);
                }

                indeg[m.Key]++;
            }
        }

        Queue<string> q = new(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        List<string> order = new();

        while (q.Count > 0)
        {
            string k = q.Dequeue();
            order.Add(k);

            foreach (string n in graph[k])
            {
                if (--indeg[n] == 0)
                {
                    q.Enqueue(n);
                }
            }
        }

        return [.. order.Select(k => byKey[k])];
    }

    public static string GetAssemblyPath(string pluginDir, PluginManifest manifest)
    {
        string asmPath = Path.Combine(
            pluginDir,
            Path.GetFileNameWithoutExtension(manifest.AssemblyFile) + ".dll"
        );

        if (!File.Exists(asmPath))
        {
            string alt =
                Directory
                    .GetFiles(pluginDir, "*.dll")
                    .FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f)
                            .Contains(manifest.Key, StringComparison.OrdinalIgnoreCase)
                    )
                ?? throw new FileNotFoundException(
                    $"No assembly for plugin {manifest.Key} in {pluginDir}"
                );
            asmPath = alt;
        }

        return asmPath;
    }
}
