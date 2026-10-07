namespace Vortex.Players.Configuration;

public sealed class PlayerPresenceConfig
{
    public const string SECTION_NAME = "Vortex:PlayerPresence";

    public int MaxOutgoingQueueSize { get; init; } = 500;
}
