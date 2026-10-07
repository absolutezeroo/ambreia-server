namespace Vortex.Rooms.Configuration;

public class PetConfig
{
    public int TickMs { get; init; } = 500;
    public int WanderIdleMinMs { get; init; } = 2500;
    public int WanderIdleMaxMs { get; init; } = 7000;
    public int WanderRadius { get; init; } = 5;
    public int WanderCandidateAttempts { get; init; } = 12;

    /// <summary>
    /// How far a companion lets its owner get before it walks back to them, in steps. Inside it, the
    /// pet wanders on its own.
    /// </summary>
    public int FollowDistance { get; init; } = 3;

    public int StatFlushIntervalMs { get; init; } = 60_000;
}
