namespace Vortex.Players.Configuration;

public sealed class PlayerPresenceConfig
{
    public const string SECTION_NAME = "Vortex:PlayerPresence";

    public int MaxOutgoingQueueSize { get; init; } = 500;

    /// <summary>
    /// How long the avatar of a player whose socket dropped stays in its room, still, waiting for a
    /// reconnect (spec Personnage §10). 0 = leave at once, as before. A Quit never waits: the room
    /// is already left when the socket closes.
    /// </summary>
    public int DisconnectGraceSeconds { get; init; } = 60;
}
