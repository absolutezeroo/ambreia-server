using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.Rpg.Content.Authoring;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Stands in for Vortex.Rpg behind the authoring routes: one sword at version 1. A write at another
/// version is stale; <see cref="NextError"/> or <see cref="NextRefusal"/> answers the next write the
/// way the planner or the validator would; every write is recorded.
/// </summary>
internal sealed class FakeContentAuthoring : IContentAuthoring
{
    private const string Sword = """{ "id": "item.weapon.sword", "kind": "item" }""";

    public List<ContentWrite> Puts { get; } = [];

    public List<(string Id, long BaseVersion, string? ReplaceWith)> Deletes { get; } = [];

    public string? NextError { get; set; }

    public bool NextRefusal { get; set; }

    public Task<ContentDocumentList?> ListAsync(string kind, CancellationToken ct) =>
        Task.FromResult(
            kind == "item"
                ? new ContentDocumentList(
                    1,
                    [
                        new ContentDocumentSummary(
                            "item.weapon.sword",
                            "item",
                            "item/sword.json",
                            false
                        ),
                    ]
                )
                : null
        );

    public Task<ContentDocument?> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(
            id == "item.weapon.sword"
                ? new ContentDocument(
                    1,
                    id,
                    "item",
                    "item/sword.json",
                    Sword,
                    new Dictionary<string, IReadOnlyDictionary<string, string>>
                    {
                        ["fr"] = new Dictionary<string, string>
                        {
                            ["item.weapon.sword.name"] = "Épée",
                        },
                    }
                )
                : null
        );

    public Task<ContentWriteResult> PutAsync(
        ContentWrite write,
        int accountId,
        CancellationToken ct
    )
    {
        Puts.Add(write);

        return Task.FromResult(Answer(write.BaseVersion));
    }

    public Task<ContentWriteResult> DeleteAsync(
        string id,
        long baseVersion,
        string? replaceWith,
        int accountId,
        CancellationToken ct
    )
    {
        Deletes.Add((id, baseVersion, replaceWith));

        return Task.FromResult(
            id == "item.weapon.sword"
                ? Answer(baseVersion)
                : ContentWriteResult.Refused("content.unknown_document")
        );
    }

    public string? SchemaOf(string kind) =>
        kind == "item" ? """{ "properties": { "category": { "enum": ["tool"] } } }""" : null;

    public Task<ContentExport> ExportAsync(CancellationToken ct) =>
        Task.FromResult(
            new ContentExport(
                1,
                [new ContentFile("item/sword.json", Sword)],
                "[]",
                new Dictionary<string, string> { ["fr"] = "item.weapon.sword.name=Épée\n" }
            )
        );

    private ContentWriteResult Answer(long baseVersion)
    {
        if (baseVersion != 1)
        {
            return ContentWriteResult.Refused(ContentWriteResult.Stale);
        }

        if (NextError is { } error)
        {
            return ContentWriteResult.Refused(error);
        }

        return new ContentWriteResult(
            NextRefusal
                ? new ContentImportResult(
                    false,
                    1,
                    0,
                    0,
                    0,
                    [
                        new ContentFinding(
                            ContentSeverity.Error,
                            "BrokenReference",
                            "item/sword.json",
                            "item.weapon.sword",
                            "x"
                        ),
                    ]
                )
                : new ContentImportResult(true, 2, 0, 1, 0, []),
            null
        );
    }
}
