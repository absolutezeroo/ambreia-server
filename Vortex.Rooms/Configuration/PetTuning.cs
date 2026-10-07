using System.Threading.Tasks;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Rooms.Configuration;

/// <summary>
/// The pet numbers an operator will want to argue with, served live from
/// <see cref="IServerConfigGrain" /> so they can be retuned from the dashboard without a restart.
/// </summary>
/// <remarks>
/// <see cref="PetConfig" /> keeps the defaults, so an untouched hotel behaves the same as before and
/// a cleared key falls back rather than reading zero.
/// </remarks>
public sealed record PetTuning
{
    public const string WanderIdleMinMsKey = "pets.wander_idle_min_ms";
    public const string WanderIdleMaxMsKey = "pets.wander_idle_max_ms";

    public required int WanderIdleMinMs { get; init; }
    public required int WanderIdleMaxMs { get; init; }

    /// <summary>The compiled defaults, used before the first read and whenever a key is unset.</summary>
    public static PetTuning FromDefaults(PetConfig config) =>
        new()
        {
            WanderIdleMinMs = config.WanderIdleMinMs,
            WanderIdleMaxMs = config.WanderIdleMaxMs,
        };

    public static async Task<PetTuning> LoadAsync(IServerConfigGrain config, PetConfig defaults) =>
        new()
        {
            WanderIdleMinMs = await config
                .GetIntAsync(WanderIdleMinMsKey, defaults.WanderIdleMinMs)
                .ConfigureAwait(false),
            WanderIdleMaxMs = await config
                .GetIntAsync(WanderIdleMaxMsKey, defaults.WanderIdleMaxMs)
                .ConfigureAwait(false),
        };
}
