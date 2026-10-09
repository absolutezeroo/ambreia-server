using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Items;

/// <summary>
/// How a bag operation of the player ended (spec Objets §17, 9007): its action and a stable code,
/// <c>ok</c> or a refusal the client translates (<c>bag_full</c>, <c>wrong_class</c>…).
/// </summary>
[GenerateSerializer, Immutable]
public sealed record RpgItemResultMessageComposer : IComposer
{
    public const int ActionMove = 1;
    public const int ActionSplit = 2;
    public const int ActionEquip = 3;
    public const int ActionUnequip = 4;
    public const int ActionDestroy = 5;
    public const int ActionClaim = 6;

    [Id(0)]
    public required int Action { get; init; }

    [Id(1)]
    public required string Code { get; init; }
}
