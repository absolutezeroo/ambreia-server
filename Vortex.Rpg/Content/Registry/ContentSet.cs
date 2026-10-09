using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Content.Registry;

/// <summary>One loaded version of the content, never modified: a reload builds a new one.</summary>
internal sealed class ContentSet(
    long version,
    FrozenDictionary<string, ContentDefinition> byId,
    FrozenDictionary<string, string> hashes
)
{
    public static readonly ContentSet Empty = new(
        0,
        FrozenDictionary<string, ContentDefinition>.Empty,
        FrozenDictionary<string, string>.Empty
    );

    // Built on first ask per type; the set never changes, so neither does a list.
    private readonly ConcurrentDictionary<Type, object> _byType = new();

    public long Version { get; } = version;

    public FrozenDictionary<string, ContentDefinition> ById { get; } = byId;

    /// <summary>Hash of each definition's canonical JSON: what a reload compares.</summary>
    public FrozenDictionary<string, string> Hashes { get; } = hashes;

    // Asked only for public kinds (the content service filters), so it stays a handful of entries.
    private readonly ConcurrentDictionary<string, string> _jsonByKind = new();

    public IReadOnlyList<T> All<T>()
        where T : ContentDefinition =>
        (IReadOnlyList<T>)
            _byType.GetOrAdd(typeof(T), _ => ById.Values.OfType<T>().OrderBy(d => d.Id).ToArray());

    /// <summary>The live definitions of one kind as a JSON array, in the shape of their files.</summary>
    public string JsonOf(string kind) =>
        _jsonByKind.GetOrAdd(
            kind,
            k =>
                "["
                + string.Join(
                    ',',
                    ById.Values.Where(d => d.Kind == k && !d.Retired)
                        .OrderBy(d => d.Id, StringComparer.Ordinal)
                        .Select(ContentJson.Serialize)
                )
                + "]"
        );
}
