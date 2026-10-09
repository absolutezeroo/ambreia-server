using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>A skill or a bar change was refused (spec M1b §6.1, 9019), to the player alone.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgSkillResultMessageComposer : IComposer
{
    public const string Dead = "dead";
    public const string Stunned = "stunned";
    public const string Casting = "casting";
    public const string UnknownSkill = "unknown_skill";
    public const string NotLearned = "not_learned";
    public const string InvalidTarget = "invalid_target";
    public const string Unreachable = "unreachable";
    public const string NoLineOfSight = "no_line_of_sight";
    public const string SafeZone = "safe_zone";
    public const string Cooldown = "cooldown";
    public const string GlobalCooldown = "global_cooldown";
    public const string NoMana = "no_mana";

    /// <summary>A bar slot out of range.</summary>
    public const string BadSlot = "bad_slot";

    [Id(0)]
    public required string SkillId { get; init; }

    [Id(1)]
    public required string Code { get; init; }
}
