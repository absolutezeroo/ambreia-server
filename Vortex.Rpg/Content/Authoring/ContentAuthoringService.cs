using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.Rpg.Content.Authoring;
using Vortex.Rpg.Content.Kinds;

namespace Vortex.Rpg.Content.Authoring;

/// <summary>
/// The content one document at a time (content authoring §4): the stored pack read, one change
/// applied by <see cref="PackEdit"/>, then the whole-pack import at the base version the author read.
/// </summary>
public sealed class ContentAuthoringService(
    StoredPackReader reader,
    IRpgContentService content,
    IEnumerable<IContentKind> kinds,
    ILogger<ContentAuthoringService> logger
) : IContentAuthoring
{
    private readonly Dictionary<string, IContentKind> _kinds = kinds.ToDictionary(
        k => k.Kind,
        StringComparer.Ordinal
    );

    public async Task<ContentDocumentList?> ListAsync(string kind, CancellationToken ct)
    {
        if (!_kinds.ContainsKey(kind))
        {
            return null;
        }

        ContentExport stored = await reader.ReadAsync(ct);

        return new ContentDocumentList(
            stored.Version,
            [
                .. stored
                    .Files.Select(f => (File: f, Document: DocumentSource.Parse(f.Text)))
                    .Where(d =>
                        Text(d.Document, "kind") == kind && Text(d.Document, "id") is not null
                    )
                    .Select(d => new ContentDocumentSummary(
                        Text(d.Document, "id")!,
                        kind,
                        d.File.Path,
                        d.Document!["retired"] is JsonValue r
                            && r.GetValueKind() == JsonValueKind.True
                    ))
                    .OrderBy(d => d.Id, StringComparer.Ordinal),
            ]
        );
    }

    public async Task<ContentDocument?> GetAsync(string id, CancellationToken ct)
    {
        ContentExport stored = await reader.ReadAsync(ct);
        ContentFile? file = stored.Files.FirstOrDefault(f => DocumentSource.IdOf(f.Text) == id);

        if (file is null)
        {
            return null;
        }

        string[] nested = PackEdit.NestedIds(stored, id);

        return new ContentDocument(
            stored.Version,
            id,
            Text(DocumentSource.Parse(file.Text), "kind") ?? string.Empty,
            file.Path,
            file.Text,
            stored.Texts.ToDictionary(
                t => t.Key,
                t => DocumentTexts.Of(t.Value, id, nested),
                StringComparer.Ordinal
            )
        );
    }

    public async Task<ContentWriteResult> PutAsync(
        ContentWrite write,
        int accountId,
        CancellationToken ct
    )
    {
        if (!_kinds.TryGetValue(write.Kind, out IContentKind? kind))
        {
            return ContentWriteResult.Refused(ContentWriteResult.UnknownKind);
        }

        ContentExport stored = await reader.ReadAsync(ct);

        if (stored.Version != write.BaseVersion)
        {
            return ContentWriteResult.Refused(ContentWriteResult.Stale);
        }

        PackEditResult edit = PackEdit.Put(
            stored,
            kind,
            write.Id,
            write.Path,
            write.Source,
            write.Texts
        );

        return await ImportAsync(edit, write.BaseVersion, accountId, "put", write.Id, ct);
    }

    public async Task<ContentWriteResult> DeleteAsync(
        string id,
        long baseVersion,
        string? replaceWith,
        int accountId,
        CancellationToken ct
    )
    {
        ContentExport stored = await reader.ReadAsync(ct);

        if (stored.Version != baseVersion)
        {
            return ContentWriteResult.Refused(ContentWriteResult.Stale);
        }

        return await ImportAsync(
            PackEdit.Remove(stored, id, replaceWith),
            baseVersion,
            accountId,
            "delete",
            id,
            ct
        );
    }

    public Task<ContentExport> ExportAsync(CancellationToken ct) => reader.ReadAsync(ct);

    private async Task<ContentWriteResult> ImportAsync(
        PackEditResult edit,
        long baseVersion,
        int accountId,
        string action,
        string id,
        CancellationToken ct
    )
    {
        if (edit.Error is { } error)
        {
            return ContentWriteResult.Refused(error);
        }

        ContentImportResult result = await content.ImportAsync(
            edit.Pack!,
            accountId,
            baseVersion,
            ct
        );

        // Another write landed between the read and the import: the gate saw it.
        if (result.Findings.Any(f => f.Code == "Stale"))
        {
            return ContentWriteResult.Refused(ContentWriteResult.Stale);
        }

        if (result.Imported)
        {
            logger.LogInformation(
                "Content {Action} of {ContentId} by account {AccountId}: version {Version}.",
                action,
                id,
                accountId,
                result.PackVersion
            );
        }

        return new ContentWriteResult(result, null);
    }

    private static string? Text(JsonObject? document, string field) =>
        document?[field] is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;
}
