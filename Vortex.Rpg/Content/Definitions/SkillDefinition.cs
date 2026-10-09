using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>A skill (spec M1b §5.1): who learns it and when, what it costs, how it is cast and aimed,
/// and the effects it applies to each target.</summary>
public sealed record SkillDefinition : ContentDefinition
{
    /// <summary>Class ids; at least one.</summary>
    public required List<string> Classes { get; init; }

    /// <summary>Specialization ids; empty = every specialization of those classes.</summary>
    public List<string> Specializations { get; init; } = [];

    /// <summary>Learned on reaching this level.</summary>
    public required int Level { get; init; }

    /// <summary>Mana.</summary>
    public int Cost { get; init; }

    public int CooldownMs { get; init; }

    /// <summary>Starts the global cooldown and waits for it.</summary>
    public bool GlobalCooldown { get; init; } = true;

    public required SkillType Type { get; init; }

    /// <summary>Required for <see cref="SkillType.Cast"/>.</summary>
    public int CastMs { get; init; }

    /// <summary>Required for <see cref="SkillType.Channel"/>, with <see cref="PulseMs"/>.</summary>
    public int ChannelMs { get; init; }

    public int PulseMs { get; init; }

    /// <summary>Tiles, Chebyshev distance; 0 for a skill on oneself or around oneself.</summary>
    public required int Range { get; init; }

    public required SkillTarget Target { get; init; }

    /// <summary>Required above 0 for an area.</summary>
    public int Radius { get; init; }

    /// <summary>Areas only.</summary>
    public int MaxTargets { get; init; } = 10;

    /// <summary>A cast that survives moving.</summary>
    public bool MoveAllowed { get; init; }

    /// <summary>No dodge roll.</summary>
    public bool Unavoidable { get; init; }

    /// <summary>Applied in this order to each target; at least one.</summary>
    public required List<SkillEffectDefinition> Effects { get; init; }

    /// <summary>Client keys.</summary>
    public string Animation { get; init; } = string.Empty;

    public string Vfx { get; init; } = string.Empty;

    public string Sound { get; init; } = string.Empty;
}
