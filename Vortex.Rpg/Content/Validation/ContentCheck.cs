using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Content.Validation;

/// <summary>What a kind's rules may look at besides the definition: the whole pack, and the furniture catalogue.</summary>
public sealed class ContentCheck(
    IReadOnlyDictionary<string, ContentDefinition> all,
    Func<int, bool> furnitureExists
)
{
    /// <summary>The definition with this id, if it exists and is of this type.</summary>
    public T? Get<T>(string id)
        where T : ContentDefinition =>
        all.TryGetValue(id, out ContentDefinition? found) ? found as T : null;

    /// <summary>Whether this id names a definition of this type.</summary>
    public bool Is<T>(string id)
        where T : ContentDefinition => Get<T>(id) is not null;

    /// <summary>Every definition of this type in the pack.</summary>
    public IEnumerable<T> All<T>()
        where T : ContentDefinition => all.Values.OfType<T>();

    public bool FurnitureExists(int definition) => furnitureExists(definition);
}
