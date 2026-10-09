using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Tests.Support;

/// <summary>Content made of the given definitions.</summary>
internal sealed class TestContent(params ContentDefinition[] definitions) : IRpgContent
{
    public long Version => 1;

    public T? Get<T>(string id)
        where T : ContentDefinition => definitions.OfType<T>().FirstOrDefault(d => d.Id == id);

    public IReadOnlyList<T> All<T>()
        where T : ContentDefinition => [.. definitions.OfType<T>()];
}
