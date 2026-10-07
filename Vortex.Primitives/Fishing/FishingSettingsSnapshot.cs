using Orleans;

namespace Vortex.Primitives.Fishing;

/// <summary>
/// The fishing system's tunables, as the grains read them.
/// </summary>
/// <remarks>
/// <para>
/// Vortex-specific: no AS3 or Habbo equivalent. These are <strong>not</strong> table rows: they are
/// admin-editable gameplay config served from <c>IServerConfigGrain</c> under the <c>fishing.*</c>
/// keys, resolved by <c>FishingConfig.ResolveAsync</c> in one round trip. That grain is
/// write-through, so an operator's edit is live on the next read — which is the whole point, since a
/// number nobody can change while the hotel is up is a number in the wrong place. See the client's
/// <c>docs/vortex-original/fishing.md</c>.
/// </para>
/// <para>
/// Only <see cref="DailyCurrencyCap"/> reaches the client, and it travels in the player-state
/// message rather than here: everything else describes how the server simulates, which the client
/// has no business predicting.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record FishingSettingsSnapshot
{
    /// <summary>Zero means uncapped.</summary>
    [Id(0)]
    public required int DailyCurrencyCap { get; init; }

    [Id(1)]
    public required int MinSightingDelayMs { get; init; }

    [Id(2)]
    public required int MaxSightingDelayMs { get; init; }

    [Id(3)]
    public required int SightingDurationMs { get; init; }

    /// <summary>Tenths of a percent off the catch rate per catch already made this session.</summary>
    [Id(4)]
    public required int SessionDecayPerCatch { get; init; }

    [Id(5)]
    public required int SessionDecayFloor { get; init; }

    [Id(6)]
    public required int FrenzyIntervalHours { get; init; }

    [Id(7)]
    public required int FrenzyDurationMinutes { get; init; }

    /// <summary>Thousandths. 5000 is the ×5 the Origins guides report.</summary>
    [Id(8)]
    public required int FrenzyXpMultiplier { get; init; }

    [Id(9)]
    public required int HookHavocDurationMs { get; init; }

    [Id(10)]
    public required int HookHavocFillRate { get; init; }

    [Id(11)]
    public required int HookHavocTolerance { get; init; }

    [Id(12)]
    public required int HookHavocTrophyHandItemId { get; init; }

    // [Id(13)] was DerbyLeaderboardSize, removed with the fishing derby (M0 bis). Never reuse it.

    /// <summary>
    /// The furni class a mounted catch becomes — one model, the catch inscribed into its stuff data.
    /// Empty disables mounting.
    /// </summary>
    [Id(14)]
    public required string TrophyFurniClass { get; init; }

    /// <summary>
    /// The avatar effect worn while a session is running: Origins' own fishing rod, rebuilt as an
    /// effect because it is anchored to the avatar rather than to the hand. Zero shows no rod.
    /// </summary>
    [Id(15)]
    public required int RodEffectId { get; init; }

    /// <summary>
    /// The compiled fallbacks, used per key when no admin override is stored — the same shape as
    /// <c>FreezeSettings.Default</c>.
    /// </summary>
    public static FishingSettingsSnapshot Defaults { get; } =
        new()
        {
            DailyCurrencyCap = 0,
            MinSightingDelayMs = 4000,
            MaxSightingDelayMs = 9000,
            SightingDurationMs = 2500,
            SessionDecayPerCatch = 0,
            SessionDecayFloor = 200,
            FrenzyIntervalHours = 4,
            FrenzyDurationMinutes = 12,
            FrenzyXpMultiplier = 5000,
            HookHavocDurationMs = 12000,
            HookHavocFillRate = 250,
            HookHavocTolerance = 12,
            HookHavocTrophyHandItemId = 2001,
            // The id effectmap.xml gives VortexFishingRod, in the 8000-8999 band no Habbo effect
            // uses. Zero here disables the rod without touching the asset host.
            RodEffectId = 8100,
            TrophyFurniClass = "",
        };
}
