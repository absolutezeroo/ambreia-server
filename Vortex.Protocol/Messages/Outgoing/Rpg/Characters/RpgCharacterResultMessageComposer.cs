using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>
/// How a character-screen request ended (spec F3a §6.2, 9002): its action, a stable code (<c>ok</c>
/// or one of §6.3, translated by the client) and the character it is about (0 when none).
/// </summary>
[GenerateSerializer, Immutable]
public sealed record RpgCharacterResultMessageComposer : IComposer
{
    public const int ActionCreate = 1;
    public const int ActionChooseClass = 2;
    public const int ActionEnterWorld = 3;

    public const string Ok = "ok";

    /// <summary>An unexpected failure, logged on the server (spec F3a §7); the socket stays open.</summary>
    public const string Failed = "character.failed";

    [Id(0)]
    public required int Action { get; init; }

    [Id(1)]
    public required string Code { get; init; }

    [Id(2)]
    public required int PlayerId { get; init; }
}
