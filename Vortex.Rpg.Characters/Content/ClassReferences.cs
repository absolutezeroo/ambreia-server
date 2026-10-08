using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Rpg.Content.Migrations;

namespace Vortex.Rpg.Characters.Content;

/// <summary>Characters hold their class (spec Personnage §12): a removed class needs a replace rule,
/// which moves them in the import's transaction. Active grains re-read the id (AGENTS.md).</summary>
public sealed class ClassReferences : IContentReferences
{
    public string Kind => "class";

    public Task<bool> IsReferencedAsync(
        VortexDbContext db,
        string contentId,
        CancellationToken ct
    ) => db.PlayerCharacters.AnyAsync(c => c.ClassId == contentId, ct);

    public Task ReplaceAsync(VortexDbContext db, string from, string to, CancellationToken ct) =>
        db
            .PlayerCharacters.Where(c => c.ClassId == from)
            .ExecuteUpdateAsync(up => up.SetProperty(c => c.ClassId, to), ct);
}
