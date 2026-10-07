using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Editing;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Stands in for Vortex.World behind the routes: the routes' job is authorization, binding and
/// status codes, which this lets the tests observe without a database.
/// </summary>
internal sealed class FakeZoneEditorService : IZoneEditorService
{
    public Task<IReadOnlyList<ZoneSummary>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ZoneSummary>>([
            new ZoneSummary("village", "Village", 7, 1, 1, null),
        ]);

    public Task<ZoneSummary> CreateAsync(
        string key,
        string name,
        int accountId,
        string? fromTemplate,
        CancellationToken ct
    ) =>
        key == "village"
            ? throw new InvalidOperationException("taken")
            : Task.FromResult(new ZoneSummary(key, name, 8, null, 0, null));

    public Task<string?> GetDocumentAsync(string key, int? version, CancellationToken ct) =>
        Task.FromResult<string?>(key == "village" ? "{\"schemaVersion\":1}" : null);

    public Task<IReadOnlyList<ZoneVersionInfo>> GetVersionsAsync(
        string key,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<ZoneVersionInfo>>([]);

    public Task<IReadOnlyList<ZoneFinding>> ValidateAsync(
        string key,
        string json,
        CancellationToken ct
    ) => Task.FromResult(Check(json));

    public Task<ZoneSaveResult> SaveAsync(
        string key,
        string json,
        int accountId,
        string comment,
        CancellationToken ct
    )
    {
        IReadOnlyList<ZoneFinding> findings = Check(json);

        return Task.FromResult(
            findings.Count == 0
                ? new ZoneSaveResult(true, 2, false, [])
                : new ZoneSaveResult(false, null, false, findings)
        );
    }

    public Task<ZoneSaveResult> PublishAsync(string key, int version, CancellationToken ct) =>
        Task.FromResult(new ZoneSaveResult(true, version, true, []));

    public Task<bool> TryLockAsync(string key, int accountId, bool force, CancellationToken ct) =>
        Task.FromResult(true);

    public Task UnlockAsync(string key, int accountId, CancellationToken ct) => Task.CompletedTask;

    public IReadOnlyList<FurniturePaletteEntry> GetPalette() =>
        [new FurniturePaletteEntry(230, "throne", false, 1, 1, 2, false, true, 1)];

    private static IReadOnlyList<ZoneFinding> Check(string json)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(json);

            return [];
        }
        catch (JsonException ex)
        {
            return [new ZoneFinding(ZoneSeverity.Error, "Json", null, ex.Message)];
        }
    }
}
