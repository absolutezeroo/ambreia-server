using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Rpg.Content.Authoring;

/// <summary>
/// The content, one document at a time (content authoring §4): read, written and removed through the
/// whole-pack import, so every write is validated, versioned and traced like a push.
/// </summary>
public interface IContentAuthoring
{
    /// <summary>The documents of a kind, by id; null for a kind the server does not know.</summary>
    public Task<ContentDocumentList?> ListAsync(string kind, CancellationToken ct);

    /// <summary>One document as authored, with its texts; null when no live document has that id.</summary>
    public Task<ContentDocument?> GetAsync(string id, CancellationToken ct);

    public Task<ContentWriteResult> PutAsync(
        ContentWrite write,
        int accountId,
        CancellationToken ct
    );

    public Task<ContentWriteResult> DeleteAsync(
        string id,
        long baseVersion,
        string? replaceWith,
        int accountId,
        CancellationToken ct
    );

    /// <summary>A kind's JSON schema, exported from its record: what an editor builds the
    /// document's form from. Null for a kind the server does not know.</summary>
    public string? SchemaOf(string kind);

    /// <summary>The stored pack, for git.</summary>
    public Task<ContentExport> ExportAsync(CancellationToken ct);
}
