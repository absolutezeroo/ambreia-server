using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Content;

/// <summary>
/// The content the server runs on (spec Contenu §10, amendment §2.1): read in memory, swapped as a
/// whole on each reload, so a reader never sees half of two versions.
/// </summary>
public interface IRpgContent
{
    /// <summary>The pack version served; 0 while nothing was ever imported.</summary>
    public long Version { get; }

    /// <summary>The definition with this id, if it exists and is of this type. Retired ones too:
    /// rows that still name them must read.</summary>
    public T? Get<T>(string id)
        where T : ContentDefinition;

    /// <summary>Every definition of this type, retired ones included.</summary>
    public IReadOnlyList<T> All<T>()
        where T : ContentDefinition;
}
