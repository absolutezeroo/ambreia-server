using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Hits;

namespace Vortex.Rpg.Combat;

/// <summary>Combat (spec M1a): its tuning and the hit steps it ships. A module that adds a step
/// (a shield, a buff) declares its own line.</summary>
public sealed class CombatModule : IHostPluginModule
{
    public string Key => "vortex-rpg-combat";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<CombatConfig>(
            builder.Configuration.GetSection(CombatConfig.SECTION_NAME)
        );

        services.AddSingleton<IHitStep, DodgeStep>();
        services.AddSingleton<IHitStep, BaseDamageStep>();
        services.AddSingleton<IHitStep, CritStep>();
        services.AddSingleton<IHitStep, ArmorStep>();
        services.AddSingleton<IHitStep, MinimumStep>();
        services.AddSingleton<HitResolver>();
    }
}
