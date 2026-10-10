using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Rpg.Content;

/// <summary>The content pipeline as the web API sees it (spec Contenu §6, §7).</summary>
public interface IRpgContentService
{
    /// <summary>Validates the whole pack and writes it in one transaction, or writes nothing.</summary>
    public Task<ContentImportResult> ImportAsync(
        ContentPack pack,
        int accountId,
        CancellationToken ct
    );

    /// <summary>The imported text file of a language, or null when none was imported.</summary>
    public Task<string?> GetTextsAsync(string language, CancellationToken ct);

    /// <summary>The definitions of a kind that says it is public (skills, statuses), not retired;
    /// null for a private or unknown kind.</summary>
    public ContentDefinitions? GetDefinitions(string kind);

    /// <summary>Every monster of the served content, retired ones included and flagged, by id: the
    /// map editor's choice for a spawn, and the zone validation's check.</summary>
    public IReadOnlyList<MonsterSummary> GetMonsters();

    /// <summary>The classes a new character may take: those of the served content, not retired.</summary>
    public IReadOnlyList<ClassSummary> GetClasses();
}
