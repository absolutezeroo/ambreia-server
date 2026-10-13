using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Database.Entities.Rpg;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>
/// Health and mana (spec M1a §7): the same everywhere. The character keeps them and writes them with
/// its progress; a zone reads them once on entry and gives them back once on exit. Out of any zone
/// they regenerate from <c>vitals_at</c>; they are always bounded to the current maximum.
/// </summary>
internal sealed partial class PlayerCharacterGrain
{
    /// <summary>The clock vitals are timed with; the tests move it.</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    private ImmutableArray<CarriedStatus> _carried = [];
    private ImmutableArray<CarriedCooldown> _carriedCooldowns = [];
    private int _carriedGlobalMs;
    private DateTimeOffset _carriedAt;

    public async Task<CharacterCombatProfile?> GetCombatProfileAsync()
    {
        if (await GetStatsAsync() is not { } stats || _character is not { } c)
        {
            return null;
        }

        long elapsed = (long)(Time.GetUtcNow() - _carriedAt).TotalMilliseconds;
        ImmutableArray<CarriedCooldown> cooldowns =
        [
            .. _carriedCooldowns
                .Where(cd => cd.RemainingMs > elapsed)
                .Select(cd => cd with { RemainingMs = (int)(cd.RemainingMs - elapsed) }),
        ];
        int global = (int)Math.Max(0, _carriedGlobalMs - elapsed);
        _carriedCooldowns = [];
        _carriedGlobalMs = 0;

        return Profile(stats, c) with
        {
            Statuses = TakeCarried(),
            Cooldowns = cooldowns,
            GlobalCooldownRemainingMs = global,
        };
    }

    public async Task PushVitalsAsync()
    {
        if (await GetStatsAsync() is not { } stats || _character is not { } c)
        {
            return;
        }

        // ponytail: once, on entry; out-of-zone regeneration is not pushed as it ticks.
        CharacterCombatProfile profile = Profile(stats, c);
        await PushAsync(
            new RpgSelfVitalsMessageComposer
            {
                Health = profile.Health,
                MaxHealth = Math.Max(1, stats.MaxHealth),
                Mana = profile.Mana,
                MaxMana = Math.Max(0, stats.MaxMana),
                InCombat = false,
            }
        );
    }

    /// <summary>The persistent statuses handed back by the last zone, less the time spent out of
    /// any zone, the expired ones dropped; given once (spec M1b §7.4).</summary>
    private ImmutableArray<CarriedStatus> TakeCarried()
    {
        long elapsed = (long)(Time.GetUtcNow() - _carriedAt).TotalMilliseconds;
        ImmutableArray<CarriedStatus> left =
        [
            .. _carried
                .Where(s => s.RemainingMs > elapsed)
                .Select(s => s with { RemainingMs = (int)(s.RemainingMs - elapsed) }),
        ];

        _carried = [];

        return left;
    }

    /// <summary>The profile read on entry, and passed on whenever the stats, level or class change.</summary>
    private CharacterCombatProfile Profile(CharacterStatsSnapshot stats, PlayerCharacterEntity c)
    {
        ClassDefinition? cls = content.Get<ClassDefinition>(c.ClassId);

        return new CharacterCombatProfile
        {
            Stats = stats,
            Health = Regenerated(c.CurrentHealth, stats.MaxHealth, c.VitalsAt),
            Mana = Regenerated(c.CurrentMana, stats.MaxMana, c.VitalsAt),
            Level = c.Level,
            Range = Math.Max(1, cls?.Range ?? 1),
            ThreatMultiplier = Math.Max(1, cls?.ThreatMultiplier ?? 1),
            ClassId = c.ClassId,
            SpecializationId = c.SpecializationId,
        };
    }

    public async Task SetVitalsAsync(
        int health,
        int mana,
        ImmutableArray<CarriedStatus> statuses,
        ImmutableArray<CarriedCooldown> cooldowns,
        int globalCooldownRemainingMs
    )
    {
        if (await GetStatsAsync() is not { } stats || _character is not { } c)
        {
            return;
        }

        // Memory only (spec M1b decision 7): a logout past the grace period loses them.
        _carried = statuses.IsDefault ? [] : statuses;
        _carriedCooldowns = cooldowns.IsDefault ? [] : cooldowns;
        _carriedGlobalMs = globalCooldownRemainingMs;
        _carriedAt = Time.GetUtcNow();

        // Full is kept as null: a maximum that grows later still finds the character full.
        c.CurrentHealth = health >= stats.MaxHealth ? null : Math.Max(0, health);
        c.CurrentMana = mana >= stats.MaxMana ? null : Math.Max(0, mana);
        c.VitalsAt = Time.GetUtcNow().UtcDateTime;
        _dirty = true;

        // The zone hands them back once, on exit: written now rather than at the next timer, so a
        // silo lost in between does not revive the character with the health it had before the fight.
        // The room awaits this call from its own turn (leaving a zone), so the write is paid by the
        // room, like the level-up's: one UPDATE per exit, and a failure stays dirty for the timer.
        await FlushAsync(CancellationToken.None);
    }

    public async Task WearEquipmentAsync(int percent)
    {
        if (await InventoryAsync() is not { } inventory || percent <= 0)
        {
            return;
        }

        // Written by the item flush, like any other change to a copy.
        (bool changed, bool broke) = inventory.WearEquipped(percent);

        if (broke)
        {
            // Its stats are gone: recomputed, announced and pushed.
            await EquipmentChangedAsync();
        }

        if (changed)
        {
            await PushAsync(InventoryComposer(inventory));
        }
    }

    /// <summary>A stored value (null: full) plus what came back since it was written, bounded.</summary>
    private int Regenerated(int? stored, int max, DateTime? since)
    {
        if (stored is not { } value)
        {
            return max;
        }

        double seconds = since is { } at ? (Time.GetUtcNow().UtcDateTime - at).TotalSeconds : 0;
        long back = (long)(Math.Max(0, seconds) * max * _config.RegenPercentPerSecond / 100);

        return (int)Math.Clamp(value + back, 0, max);
    }
}
