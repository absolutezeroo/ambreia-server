using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;

/// <summary>
/// The player's target (spec F3b §7.2, 9009): the room object, its kind, its name, level and class
/// — or <see cref="None"/> once it is cleared. Sent to that player alone.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record RpgTargetMessageComposer : IComposer
{
    public const string KindPlayer = "player";

    /// <summary>A monster (spec M1a §9.1): its name is its content id, its class empty.</summary>
    public const string KindMonster = "monster";

    /// <summary>No target: cleared by the player, an invalid id, or the target leaving.</summary>
    public static readonly RpgTargetMessageComposer None = new()
    {
        ObjectId = 0,
        Kind = string.Empty,
        Name = string.Empty,
        Level = 0,
        ClassId = string.Empty,
    };

    [Id(0)]
    public required int ObjectId { get; init; }

    [Id(1)]
    public required string Kind { get; init; }

    [Id(2)]
    public required string Name { get; init; }

    /// <summary>0 when the target's character did not answer.</summary>
    [Id(3)]
    public required int Level { get; init; }

    [Id(4)]
    public required string ClassId { get; init; }
}
