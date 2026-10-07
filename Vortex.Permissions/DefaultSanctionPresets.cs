using System.Collections.Generic;
using Vortex.Primitives.Permissions;

namespace Vortex.Permissions;

/// <summary>
/// Default sanction-preset bootstrap. Seeded only into a fresh database (see
/// <see cref="SanctionPresetSeederService"/>); once a preset exists it's administrator-managed and
/// no longer overwritten. Duration values are a reasonable default matching common Habbo ban-tier
/// conventions — not confirmed against the WIN63 client's own preset labels, since the client sends
/// only a raw index (see <see cref="SanctionPresetKind"/>). Tune via the admin dashboard if the
/// staff-visible durations don't match what the client's dropdown actually shows.
/// </summary>
internal static class DefaultSanctionPresets
{
    public sealed record PresetSeed(
        SanctionPresetKind Kind,
        int PresetIndex,
        string Name,
        int? DurationSeconds
    );

    public static readonly IReadOnlyList<PresetSeed> All =
    [
        new PresetSeed(SanctionPresetKind.Ban, 0, "2 hours", 2 * 3600),
        new PresetSeed(SanctionPresetKind.Ban, 1, "1 day", 24 * 3600),
        new PresetSeed(SanctionPresetKind.Ban, 2, "3 days", 3 * 24 * 3600),
        new PresetSeed(SanctionPresetKind.Ban, 3, "1 week", 7 * 24 * 3600),
        new PresetSeed(SanctionPresetKind.Ban, 4, "1 month", 30 * 24 * 3600),
        new PresetSeed(SanctionPresetKind.Ban, 5, "Permanent", null),
        new PresetSeed(SanctionPresetKind.TradingLock, 0, "1 day", 24 * 3600),
        new PresetSeed(SanctionPresetKind.TradingLock, 1, "1 week", 7 * 24 * 3600),
        new PresetSeed(SanctionPresetKind.TradingLock, 2, "1 month", 30 * 24 * 3600),
        new PresetSeed(SanctionPresetKind.TradingLock, 3, "Permanent", null),
    ];
}
