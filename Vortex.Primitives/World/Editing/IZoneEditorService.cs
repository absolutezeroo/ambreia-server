using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.World.Editing;

/// <summary>Everything the map editor does to zones, behind the web API's /api/editor routes.</summary>
public interface IZoneEditorService
{
    Task<IReadOnlyList<ZoneSummary>> ListAsync(CancellationToken ct);

    /// <exception cref="System.ArgumentException">The key is not 3–64 of [a-z0-9_].</exception>
    /// <exception cref="System.InvalidOperationException">The key is taken.</exception>
    Task<ZoneSummary> CreateAsync(
        string key,
        string name,
        int accountId,
        string? fromTemplate,
        CancellationToken ct
    );

    /// <summary>A version's JSON; <paramref name="version"/> null = the published one.</summary>
    Task<string?> GetDocumentAsync(string key, int? version, CancellationToken ct);

    Task<IReadOnlyList<ZoneVersionInfo>> GetVersionsAsync(string key, CancellationToken ct);

    Task<IReadOnlyList<ZoneFinding>> ValidateAsync(string key, string json, CancellationToken ct);

    Task<ZoneSaveResult> SaveAsync(
        string key,
        string json,
        int accountId,
        string comment,
        CancellationToken ct
    );

    Task<ZoneSaveResult> PublishAsync(string key, int version, CancellationToken ct);

    Task<bool> TryLockAsync(string key, int accountId, bool force, CancellationToken ct);

    Task UnlockAsync(string key, int accountId, CancellationToken ct);

    IReadOnlyList<FurniturePaletteEntry> GetPalette();
}
