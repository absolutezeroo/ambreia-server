using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Rpg.Content.Migrations;

namespace Vortex.Rpg.Characters.Content;

/// <summary>Characters hold item definitions (spec Objets §11): a removed one needs a replace rule,
/// which moves every copy in the import's transaction. Active grains re-read the ids when the
/// content version moves (AGENTS.md).</summary>
public sealed class ItemReferences : IContentReferences
{
    public string Kind => "item";

    public Task<bool> IsReferencedAsync(
        VortexDbContext db,
        string contentId,
        CancellationToken ct
    ) => db.RpgItems.AnyAsync(i => i.DefinitionId == contentId && i.DeletedAt == null, ct);

    public Task ReplaceAsync(VortexDbContext db, string from, string to, CancellationToken ct) =>
        db
            .RpgItems.Where(i => i.DefinitionId == from)
            .ExecuteUpdateAsync(up => up.SetProperty(i => i.DefinitionId, to), ct);
}
