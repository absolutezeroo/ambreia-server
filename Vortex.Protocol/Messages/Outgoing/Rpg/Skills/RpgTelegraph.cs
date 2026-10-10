using System.Collections.Immutable;
using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>A cast that marks its ground (spec M1c §9.2): the cells it will touch, fixed at its start,
/// and the time left before it lands. The client clears it on the caster's <c>RpgCastEnd</c>.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgTelegraph
{
    public const string ShapeCircle = "circle";
    public const string ShapeLine = "line";
    public const string ShapeCone = "cone";

    [Id(0)]
    public required int CasterObjectId { get; init; }

    [Id(1)]
    public required string SkillId { get; init; }

    [Id(2)]
    public required string Shape { get; init; }

    [Id(3)]
    public required int DelayMs { get; init; }

    [Id(4)]
    public required ImmutableArray<(int X, int Y)> Cells { get; init; }
}
