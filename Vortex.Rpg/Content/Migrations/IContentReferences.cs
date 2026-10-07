using System.Threading;
using System.Threading.Tasks;
using Vortex.Database.Context;

namespace Vortex.Rpg.Content.Migrations;

/// <summary>
/// Where players hold ids of one kind (spec Contenu §8): a character's class, an item's definition.
/// Provided by the system that stores them; a kind without one is referenced nowhere.
/// </summary>
public interface IContentReferences
{
    /// <summary>The kind whose ids these references hold (<c>item</c>, <c>class</c>).</summary>
    public string Kind { get; }

    public Task<bool> IsReferencedAsync(VortexDbContext db, string contentId, CancellationToken ct);

    /// <summary>Points every reference to <paramref name="from"/> at <paramref name="to"/>, inside
    /// the import's transaction (<paramref name="db"/> carries it).</summary>
    public Task ReplaceAsync(VortexDbContext db, string from, string to, CancellationToken ct);
}
