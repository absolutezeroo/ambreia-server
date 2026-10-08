using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Rpg.Content.Migrations;

namespace Vortex.Rpg.Characters.Content;

/// <summary>Characters hold their specialization: same rules as <see cref="ClassReferences"/>.</summary>
public sealed class SpecializationReferences : IContentReferences
{
    public string Kind => "specialization";

    public Task<bool> IsReferencedAsync(
        VortexDbContext db,
        string contentId,
        CancellationToken ct
    ) => db.PlayerCharacters.AnyAsync(c => c.SpecializationId == contentId, ct);

    public Task ReplaceAsync(VortexDbContext db, string from, string to, CancellationToken ct) =>
        db
            .PlayerCharacters.Where(c => c.SpecializationId == from)
            .ExecuteUpdateAsync(up => up.SetProperty(c => c.SpecializationId, to), ct);
}
