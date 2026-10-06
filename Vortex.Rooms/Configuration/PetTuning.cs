using System.Threading.Tasks;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Rooms.Configuration;

/// <summary>
/// The pet numbers an operator will want to argue with, served live from
/// <see cref="IServerConfigGrain" /> so they can be retuned from the dashboard without a restart.
/// </summary>
/// <remarks>
/// <para>
/// None of these rates are published anywhere. Habbo's own guides describe the behaviour -- a tired
/// pet goes to its basket, toys and commands cheer it up -- but never a speed,
/// and the values below are read off Arcturus, which is itself a reimplementation. They are informed
/// guesses, which is exactly the kind of number that belongs in a table an admin can edit rather
/// than compiled into the build.
/// </para>
/// <para>
/// <see cref="PetConfig" /> keeps the defaults, so an untouched hotel behaves the same as before and
/// a cleared key falls back rather than reading zero.
/// </para>
/// </remarks>
public sealed record PetTuning
{
    public const string HappinessDecayPerMinuteKey = "pets.happiness_decay_per_minute";
    public const string HappinessRestGainPerMinuteKey = "pets.happiness_rest_gain_per_minute";
    public const string RespectHappinessRewardKey = "pets.respect_happiness_reward";
    public const string CommandHappinessRewardKey = "pets.command_happiness_reward";
    public const string CommandObeyBaseChancePercentKey = "pets.command_obey_base_chance_percent";
    public const string CommandObeyChancePerLevelPercentKey =
        "pets.command_obey_chance_per_level_percent";
    public const string CommandObeyMaxChancePercentKey = "pets.command_obey_max_chance_percent";
    public const string ToyHappinessRewardKey = "pets.toy_happiness_reward";
    public const string BoredHappinessThresholdKey = "pets.bored_happiness_threshold";
    public const string ToyPlayDurationMsKey = "pets.toy_play_duration_ms";
    public const string ToyPlayCooldownMsKey = "pets.toy_play_cooldown_ms";
    public const string ToyPlayChancePercentKey = "pets.toy_play_chance_percent";
    public const string WanderIdleMinMsKey = "pets.wander_idle_min_ms";
    public const string WanderIdleMaxMsKey = "pets.wander_idle_max_ms";
    public const string VocalIntervalMsKey = "pets.vocal_interval_ms";

    public required double HappinessDecayPerMinute { get; init; }
    public required double HappinessRestGainPerMinute { get; init; }
    public required int RespectHappinessReward { get; init; }
    public required int CommandHappinessReward { get; init; }
    public required int CommandObeyBaseChancePercent { get; init; }
    public required int CommandObeyChancePerLevelPercent { get; init; }
    public required int CommandObeyMaxChancePercent { get; init; }
    public required int ToyHappinessReward { get; init; }
    public required int BoredHappinessThreshold { get; init; }
    public required int ToyPlayDurationMs { get; init; }
    public required int ToyPlayCooldownMs { get; init; }
    public required int ToyPlayChancePercent { get; init; }
    public required int WanderIdleMinMs { get; init; }
    public required int WanderIdleMaxMs { get; init; }
    public required int VocalIntervalMs { get; init; }

    /// <summary>The compiled defaults, used before the first read and whenever a key is unset.</summary>
    public static PetTuning FromDefaults(PetConfig config) =>
        new()
        {
            HappinessDecayPerMinute = config.HappinessDecayPerMinute,
            HappinessRestGainPerMinute = config.HappinessRestGainPerMinute,
            RespectHappinessReward = config.RespectHappinessReward,
            CommandHappinessReward = config.CommandHappinessReward,
            CommandObeyBaseChancePercent = config.CommandObeyBaseChancePercent,
            CommandObeyChancePerLevelPercent = config.CommandObeyChancePerLevelPercent,
            CommandObeyMaxChancePercent = config.CommandObeyMaxChancePercent,
            ToyHappinessReward = config.ToyHappinessReward,
            BoredHappinessThreshold = config.BoredHappinessThreshold,
            ToyPlayDurationMs = config.ToyPlayDurationMs,
            ToyPlayCooldownMs = config.ToyPlayCooldownMs,
            ToyPlayChancePercent = config.ToyPlayChancePercent,
            WanderIdleMinMs = config.WanderIdleMinMs,
            WanderIdleMaxMs = config.WanderIdleMaxMs,
            VocalIntervalMs = config.VocalIntervalMs,
        };

    public static async Task<PetTuning> LoadAsync(IServerConfigGrain config, PetConfig defaults) =>
        new()
        {
            HappinessDecayPerMinute = await config
                .GetDoubleAsync(HappinessDecayPerMinuteKey, defaults.HappinessDecayPerMinute)
                .ConfigureAwait(false),
            HappinessRestGainPerMinute = await config
                .GetDoubleAsync(HappinessRestGainPerMinuteKey, defaults.HappinessRestGainPerMinute)
                .ConfigureAwait(false),
            RespectHappinessReward = await config
                .GetIntAsync(RespectHappinessRewardKey, defaults.RespectHappinessReward)
                .ConfigureAwait(false),
            CommandHappinessReward = await config
                .GetIntAsync(CommandHappinessRewardKey, defaults.CommandHappinessReward)
                .ConfigureAwait(false),
            CommandObeyBaseChancePercent = await config
                .GetIntAsync(CommandObeyBaseChancePercentKey, defaults.CommandObeyBaseChancePercent)
                .ConfigureAwait(false),
            CommandObeyChancePerLevelPercent = await config
                .GetIntAsync(
                    CommandObeyChancePerLevelPercentKey,
                    defaults.CommandObeyChancePerLevelPercent
                )
                .ConfigureAwait(false),
            CommandObeyMaxChancePercent = await config
                .GetIntAsync(CommandObeyMaxChancePercentKey, defaults.CommandObeyMaxChancePercent)
                .ConfigureAwait(false),
            ToyHappinessReward = await config
                .GetIntAsync(ToyHappinessRewardKey, defaults.ToyHappinessReward)
                .ConfigureAwait(false),
            BoredHappinessThreshold = await config
                .GetIntAsync(BoredHappinessThresholdKey, defaults.BoredHappinessThreshold)
                .ConfigureAwait(false),
            ToyPlayDurationMs = await config
                .GetIntAsync(ToyPlayDurationMsKey, defaults.ToyPlayDurationMs)
                .ConfigureAwait(false),
            ToyPlayCooldownMs = await config
                .GetIntAsync(ToyPlayCooldownMsKey, defaults.ToyPlayCooldownMs)
                .ConfigureAwait(false),
            ToyPlayChancePercent = await config
                .GetIntAsync(ToyPlayChancePercentKey, defaults.ToyPlayChancePercent)
                .ConfigureAwait(false),
            WanderIdleMinMs = await config
                .GetIntAsync(WanderIdleMinMsKey, defaults.WanderIdleMinMs)
                .ConfigureAwait(false),
            WanderIdleMaxMs = await config
                .GetIntAsync(WanderIdleMaxMsKey, defaults.WanderIdleMaxMs)
                .ConfigureAwait(false),
            VocalIntervalMs = await config
                .GetIntAsync(VocalIntervalMsKey, defaults.VocalIntervalMs)
                .ConfigureAwait(false),
        };
}
