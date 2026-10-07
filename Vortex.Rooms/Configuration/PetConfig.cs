namespace Vortex.Rooms.Configuration;

public class PetConfig
{
    public int TickMs { get; init; } = 500;
    public int WanderIdleMinMs { get; init; } = 2500;
    public int WanderIdleMaxMs { get; init; } = 7000;
    public int WanderRadius { get; init; } = 5;
    public int WanderCandidateAttempts { get; init; } = 12;

    /// <summary>
    /// Mood drains on its own clock while the pet is up, and comes back while it rests. Two a minute
    /// awake is what Habbo does (one every thirty seconds); resting pays it back twice as fast, so a
    /// nap is worth taking.
    /// </summary>
    public double HappinessDecayPerMinute { get; init; } = 2.0;
    public double HappinessRestGainPerMinute { get; init; } = 4.0;
    public int HappinessCap { get; init; } = 100;
    public int StatFlushIntervalMs { get; init; } = 60_000;

    /// <summary>
    /// How long a pet sent to its nest (command 13) naps there before it gets back up. Pets have no
    /// fatigue since M0, so the nap is a fixed length rather than "until rested".
    /// </summary>
    public int NestNapMs { get; init; } = 60_000;

    public int VocalIntervalMs { get; init; } = 14_000;

    public int CommandXpReward { get; init; } = 3;

    /// <summary>
    /// Obeying pays nothing. A capture of the live hotel caught a pet obeying three commands with
    /// its happiness pinned at 35 the whole way through, so this is 0 rather than the 5 it guessed.
    /// Scratching and toys are what lift a pet's mood; the knob stays because a hotel may disagree.
    /// </summary>
    public int CommandHappinessReward { get; init; }

    /// <summary>
    /// How often a pet does as it is told, before the bonus below. A capture of the live hotel
    /// (room-pet-commands-official-001) caught a level 8 pet ignoring four orders out of nine, each
    /// with a line of its own, so the refusal is real -- but nine samples name no rate and no rule.
    /// 70 is the figure Arcturus settled on, kept here as the assumption it is. Set it to 100 and a
    /// pet always obeys, which is what this emulator did before.
    /// </summary>
    public int CommandObeyBaseChancePercent { get; init; } = 70;

    /// <summary>What each level above a command's own requirement adds to the chance above. A pet
    /// that has long outgrown a trick rarely refuses it.</summary>
    public int CommandObeyChancePerLevelPercent { get; init; } = 5;

    /// <summary>The ceiling on the two above. Never 100: a pet that cannot refuse is furniture.
    /// </summary>
    public int CommandObeyMaxChancePercent { get; init; } = 95;

    /// <summary>What a toy is worth. Habbo's guides say toys are what cheer a pet up; 25 is the
    /// figure Arcturus pays for a ball or a trampoline.</summary>
    public int ToyHappinessReward { get; init; } = 25;

    /// <summary>Below this, a pet goes looking for a toy rather than wandering aimlessly.</summary>
    public int BoredHappinessThreshold { get; init; } = 50;

    /// <summary>How long a pet stays on the toy, playing, before it gets up again.</summary>
    public int ToyPlayDurationMs { get; init; } = 4000;

    /// <summary>
    /// How long before the same pet can be paid for a toy again. Without it, a pet that happened to
    /// wander back and forth across a ball would top its mood up for free.
    /// </summary>
    public int ToyPlayCooldownMs { get; init; } = 60_000;

    /// <summary>
    /// The chance, each time a pet decides what to do next, that it goes to play for no reason at
    /// all. Boredom is why it must; this is why it sometimes does anyway.
    /// </summary>
    public int ToyPlayChancePercent { get; init; } = 15;
}
