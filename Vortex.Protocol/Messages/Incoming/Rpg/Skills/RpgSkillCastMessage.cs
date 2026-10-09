using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Skills;

/// <summary>The player casts a skill (spec M1b §10.1, 9013): at a room object (0: none) or a cell
/// (−1, −1: none), as the skill's target asks.</summary>
public record RpgSkillCastMessage : IMessageEvent
{
    public required string SkillId { get; init; }

    public required int TargetObjectId { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }
}
