using System;
using System.Threading.Tasks;
using Vortex.Primitives.Rpg.Characters;
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

    public async Task<CharacterCombatProfile?> GetCombatProfileAsync()
    {
        if (await GetStatsAsync() is not { } stats || _character is not { } c)
        {
            return null;
        }

        ClassDefinition? cls = content.Get<ClassDefinition>(c.ClassId);

        return new CharacterCombatProfile
        {
            Stats = stats,
            Health = Regenerated(c.CurrentHealth, stats.MaxHealth, c.VitalsAt),
            Mana = Regenerated(c.CurrentMana, stats.MaxMana, c.VitalsAt),
            Level = c.Level,
            Range = Math.Max(1, cls?.Range ?? 1),
            ThreatMultiplier = Math.Max(1, cls?.ThreatMultiplier ?? 1),
        };
    }

    public async Task SetVitalsAsync(int health, int mana)
    {
        if (await GetStatsAsync() is not { } stats || _character is not { } c)
        {
            return;
        }

        // Full is kept as null: a maximum that grows later still finds the character full.
        c.CurrentHealth = health >= stats.MaxHealth ? null : Math.Max(0, health);
        c.CurrentMana = mana >= stats.MaxMana ? null : Math.Max(0, mana);
        c.VitalsAt = Time.GetUtcNow().UtcDateTime;
        _dirty = true;
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
