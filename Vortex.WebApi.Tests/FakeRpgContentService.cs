using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rpg.Content;

namespace Vortex.WebApi.Tests;

/// <summary>Stands in for Vortex.Rpg behind the content routes: a pack with files is imported as
/// version 1, an empty one is refused; only French texts exist.</summary>
internal sealed class FakeRpgContentService : IRpgContentService
{
    public Task<ContentImportResult> ImportAsync(
        ContentPack pack,
        int accountId,
        CancellationToken ct
    ) =>
        Task.FromResult(
            pack.Files.Count > 0
                ? new ContentImportResult(true, 1, pack.Files.Count, 0, 0, [])
                : new ContentImportResult(
                    false,
                    0,
                    0,
                    0,
                    0,
                    [new ContentFinding(ContentSeverity.Error, "NoFrenchTexts", null, null, "x")]
                )
        );

    public Task<string?> GetTextsAsync(string language, CancellationToken ct) =>
        Task.FromResult(language == "fr" ? "class.warrior.name=Guerrier" : null);

    public IReadOnlyList<ClassSummary> GetClasses() =>
        [
            new ClassSummary(
                "class.classless",
                "damage",
                1,
                "strength",
                ["spec.classless.adventurer"],
                Starter: true
            ),
            new ClassSummary("class.warrior", "tank", 1, "strength", ["spec.warrior.guardian"]),
        ];
}
