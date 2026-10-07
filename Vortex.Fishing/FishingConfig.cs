using System.Collections.Immutable;
using System.Threading.Tasks;
using Vortex.Primitives.Fishing;
using Vortex.Primitives.Server;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Fishing;

/// <summary>
/// Admin-editable config keys and defaults for the fishing system, served live from
/// <see cref="IServerConfigGrain"/> — the same pattern as <c>FreezeConfig</c> / <c>GroupConfig</c> /
/// <c>MembershipConfig</c>. Each key's default is the fallback when no admin override is stored, and
/// <see cref="ResolveAsync"/> reads the whole set in one round trip.
/// </summary>
/// <remarks>
/// <para>
/// These are the knobs, not the content. Species, zones, rod tiers and the level curve are reference
/// data and live in tables an operator fills; the numbers here describe how the simulation behaves
/// and are exactly what <see cref="IServerConfigGrain"/> exists for — writes are write-through, so an
/// edit is live on the next read with no reload and no restart.
/// </para>
/// <para>
/// The frenzy values are Origins' as the guides report them: every four hours on the hour, ten to
/// fifteen minutes, ×5 XP, every catch triggering Hook Havoc. The caps are not — Origins documents no
/// daily ceiling or anti-idle measure, and since fishing here runs unattended one is worth having.
/// It defaults to off. See the client's <c>docs/vortex-original/fishing.md</c>.
/// </para>
/// </remarks>
public static class FishingConfig
{
    public const string DailyCurrencyCapKey = "fishing.daily_currency_cap";
    public const string MinSightingDelayKey = "fishing.min_sighting_delay_ms";
    public const string MaxSightingDelayKey = "fishing.max_sighting_delay_ms";
    public const string SightingDurationKey = "fishing.sighting_duration_ms";
    public const string SessionDecayPerCatchKey = "fishing.session_decay_per_catch";
    public const string SessionDecayFloorKey = "fishing.session_decay_floor";
    public const string FrenzyIntervalHoursKey = "fishing.frenzy_interval_hours";
    public const string FrenzyDurationMinutesKey = "fishing.frenzy_duration_minutes";
    public const string FrenzyXpMultiplierKey = "fishing.frenzy_xp_multiplier";
    public const string HookHavocDurationKey = "fishing.hook_havoc_duration_ms";
    public const string HookHavocFillRateKey = "fishing.hook_havoc_fill_rate";
    public const string HookHavocToleranceKey = "fishing.hook_havoc_tolerance";
    public const string HookHavocTrophyHandItemKey = "fishing.hook_havoc_trophy_hand_item_id";
    public const string TrophyFurniClassKey = "fishing.trophy_furni_class";
    public const string RodEffectIdKey = "fishing.rod_effect_id";

    /// <summary>Every key of the group, for the one-round-trip batch resolve.</summary>
    public static readonly ImmutableArray<string> AllKeys =
    [
        DailyCurrencyCapKey,
        MinSightingDelayKey,
        MaxSightingDelayKey,
        SightingDurationKey,
        SessionDecayPerCatchKey,
        SessionDecayFloorKey,
        FrenzyIntervalHoursKey,
        FrenzyDurationMinutesKey,
        FrenzyXpMultiplierKey,
        HookHavocDurationKey,
        HookHavocFillRateKey,
        HookHavocToleranceKey,
        HookHavocTrophyHandItemKey,
        TrophyFurniClassKey,
        RodEffectIdKey,
    ];

    /// <summary>
    /// Reads the live tunables in a single grain round trip, falling back to the compiled defaults.
    /// </summary>
    public static async Task<FishingSettingsSnapshot> ResolveAsync(IServerConfigGrain config)
    {
        FishingSettingsSnapshot d = FishingSettingsSnapshot.Defaults;
        ImmutableDictionary<string, string> v = await config
            .GetManyAsync(AllKeys)
            .ConfigureAwait(false);

        return new FishingSettingsSnapshot
        {
            DailyCurrencyCap = ServerConfigValues.GetInt(
                v,
                DailyCurrencyCapKey,
                d.DailyCurrencyCap
            ),
            MinSightingDelayMs = ServerConfigValues.GetInt(
                v,
                MinSightingDelayKey,
                d.MinSightingDelayMs
            ),
            MaxSightingDelayMs = ServerConfigValues.GetInt(
                v,
                MaxSightingDelayKey,
                d.MaxSightingDelayMs
            ),
            SightingDurationMs = ServerConfigValues.GetInt(
                v,
                SightingDurationKey,
                d.SightingDurationMs
            ),
            SessionDecayPerCatch = ServerConfigValues.GetInt(
                v,
                SessionDecayPerCatchKey,
                d.SessionDecayPerCatch
            ),
            SessionDecayFloor = ServerConfigValues.GetInt(
                v,
                SessionDecayFloorKey,
                d.SessionDecayFloor
            ),
            FrenzyIntervalHours = ServerConfigValues.GetInt(
                v,
                FrenzyIntervalHoursKey,
                d.FrenzyIntervalHours
            ),
            FrenzyDurationMinutes = ServerConfigValues.GetInt(
                v,
                FrenzyDurationMinutesKey,
                d.FrenzyDurationMinutes
            ),
            FrenzyXpMultiplier = ServerConfigValues.GetInt(
                v,
                FrenzyXpMultiplierKey,
                d.FrenzyXpMultiplier
            ),
            HookHavocDurationMs = ServerConfigValues.GetInt(
                v,
                HookHavocDurationKey,
                d.HookHavocDurationMs
            ),
            HookHavocFillRate = ServerConfigValues.GetInt(
                v,
                HookHavocFillRateKey,
                d.HookHavocFillRate
            ),
            HookHavocTolerance = ServerConfigValues.GetInt(
                v,
                HookHavocToleranceKey,
                d.HookHavocTolerance
            ),
            HookHavocTrophyHandItemId = ServerConfigValues.GetInt(
                v,
                HookHavocTrophyHandItemKey,
                d.HookHavocTrophyHandItemId
            ),
            TrophyFurniClass = ServerConfigValues.GetString(
                v,
                TrophyFurniClassKey,
                d.TrophyFurniClass
            ),
            RodEffectId = ServerConfigValues.GetInt(v, RodEffectIdKey, d.RodEffectId),
        };
    }
}
