namespace Vortex.Rooms.Configuration;

public class PetConfig
{
    public int TickMs { get; init; } = 500;
    public int WanderIdleMinMs { get; init; } = 2500;
    public int WanderIdleMaxMs { get; init; } = 7000;
    public int WanderRadius { get; init; } = 5;
    public int WanderCandidateAttempts { get; init; } = 12;
    public int NutritionCap { get; init; } = 100;
    public int EnergyCap { get; init; } = 100;
    public double NutritionDecayPerMinute { get; init; } = 1.0;
    public double EnergyDecayPerMinute { get; init; } = 0.5;

    /// <summary>Water drains on its own clock, a little slower than food.</summary>
    public double ThirstDecayPerMinute { get; init; } = 0.75;

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
    /// How long a monsterplant's well-being takes to drain from full to nothing without being
    /// treated. 24 hours is not a guess: the client's own pet-info view counts down from
    /// <c>MaxWellBeingSeconds</c>, and it is sent 86 400.
    /// </summary>
    public int PlantWellBeingSeconds { get; init; } = 86_400;

    /// <summary>
    /// How long a watered plant takes to put on one growth stage. Nothing publishes Habbo's own
    /// figure, so this is the knob an operator will want first -- seven stages at twelve hours is
    /// three days from seed to full size.
    /// </summary>
    public int PlantGrowthSeconds { get; init; } = 43_200;

    /// <summary>Growth stages a fertilizer skips at once.</summary>
    public int PlantFertilizerLevels { get; init; } = 1;

    /// <summary>Energy at which a sleeping pet has rested enough to get back up.</summary>
    public int SleepWakeEnergyThreshold { get; init; } = 40;

    /// <summary>
    /// Energy at which a pet reads as tired: it stops wandering, heads for the nearest free nest and
    /// naps there. Must stay below <see cref="SleepWakeEnergyThreshold" />, otherwise a pet would
    /// wake on the same tick it fell asleep.
    /// </summary>
    public int TiredEnergyThreshold { get; init; } = 20;

    public double NestEnergyMultiplier { get; init; } = 2.0;
    public int HungerThreshold { get; init; } = 50;
    public int ThirstThreshold { get; init; } = 50;
    public int VocalIntervalMs { get; init; } = 14_000;
    public int MaxWellBeingSeconds { get; init; } = 86_400;

    /// <summary>
    /// How old an account must be, in days, before it may respect a pet. Habbo gates this; the
    /// default of 0 leaves the gate open, so a hotel opts in rather than inheriting a rule it never
    /// asked for.
    /// </summary>
    public int RespectMinimumAccountAgeDays { get; init; }

    /// <summary>What a scratch pays. 10, measured on the live hotel (PetExperience/3712).</summary>
    public int RespectXpReward { get; init; } = 10;

    /// <summary>A scratch is also the main way a pet is cheered up: the live hotel raises the
    /// happiness bar by 10 per scratch, measured twice on the same pet.</summary>
    public int RespectHappinessReward { get; init; } = 10;

    public int CommandXpReward { get; init; } = 3;

    /// <summary>Obeying pleases a pet. Habbo pays 5 to 25 depending on how fun the trick is.</summary>
    public int CommandHappinessReward { get; init; } = 5;

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

    /// <summary>A pet below this has no energy to play. Habbo refuses at 35 or under.</summary>
    public int PlayEnergyThreshold { get; init; } = 35;

    /// <summary>
    /// The chance, each time a pet decides what to do next, that it goes to play for no reason at
    /// all. Boredom is why it must; this is why it sometimes does anyway.
    /// </summary>
    public int ToyPlayChancePercent { get; init; } = 15;
    public int SupplementEnergyBoost { get; init; } = 30;
    public int SupplementXpReward { get; init; } = 5;
}
