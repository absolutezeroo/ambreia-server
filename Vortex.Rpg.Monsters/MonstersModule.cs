using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Plugins;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Configuration;
using Vortex.Rpg.Monsters.Zones;
using Vortex.Rpg.Zones;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Monsters;

/// <summary>Monsters (spec M1a §5): the behaviors it ships and the zone simulation that runs them
/// with combat. A module that brings a behavior declares one line, as here: the content import sees
/// it through <see cref="DeclaredBehaviors"/>.</summary>
public sealed class MonstersModule : IHostPluginModule
{
    public string Key => "vortex-rpg-monsters";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        // Spec M1c §10: a value that could not work refuses the start, naming its key.
        services
            .AddOptions<MonstersConfig>()
            .Bind(builder.Configuration.GetSection(MonstersConfig.SECTION_NAME))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MonstersConfig>, MonstersConfigValidator>();

        // Spec M1a §5.3, M1c §4.3: the kinds it ships, by their key.
        services.AddSingleton<IMonsterBehavior, MeleeBehavior>();
        services.AddSingleton<IMonsterBehavior, RangedBehavior>();
        services.AddSingleton<IMonsterBehavior, CasterBehavior>();
        services.AddSingleton<IMonsterBehaviorKeys, DeclaredBehaviors>();
        services.AddSingleton<IZoneSimulationFactory, RpgZoneSimulationFactory>();

        // Spec Z1 §6.1: the zone action that wakes this module's dormant spawns.
        services.AddZoneAction<SpawnGroupExecutor>();
    }
}
