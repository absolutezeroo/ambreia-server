using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Combat;

/// <summary>Le choix d'un joueur KO (M1d §2.3, 9017).</summary>
public record RpgDeathChoiceMessage : IMessageEvent
{
    public const string Wait = "wait";
    public const string Release = "release";
    public const string Accept = "accept";
    public const string Decline = "decline";

    public required string Choice { get; init; }
}
