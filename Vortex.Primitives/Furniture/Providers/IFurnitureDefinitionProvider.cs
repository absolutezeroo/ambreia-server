using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Furniture.Snapshots;

namespace Vortex.Primitives.Furniture.Providers;

public interface IFurnitureDefinitionProvider
{
    public FurnitureDefinitionSnapshot? TryGetDefinition(int id);

    /// <summary>
    /// Looks a definition up by its classname. Needed wherever the furniture is chosen by what it
    /// is rather than by an id someone configured — gift wrapping picks <c>present_gen*</c> from the
    /// box type the client sends, and no table maps the two.
    /// </summary>
    public FurnitureDefinitionSnapshot? TryGetDefinitionByName(string name);
    public Task ReloadAsync(CancellationToken ct);

    /// <summary>Every loaded definition: the map editor's palette.</summary>
    public IReadOnlyCollection<FurnitureDefinitionSnapshot> All();
}
