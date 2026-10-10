using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Zones;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters;

/// <summary>Monsters (spec M1a §5): the behaviors it ships and the zone simulation that runs them
/// with combat. A module that brings a behavior (M1c) declares its own line.</summary>
public sealed class MonstersModule : IHostPluginModule
{
    public string Key => "vortex-rpg-monsters";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        AddBehavior<MeleeBehavior>(services);
        services.AddSingleton<IZoneSimulationFactory, RpgZoneSimulationFactory>();
    }

    /// <summary>One instance, seen as a behavior by the zones and as a key by the content import.</summary>
    public static void AddBehavior<T>(IServiceCollection services)
        where T : class, IMonsterBehavior
    {
        services.AddSingleton<T>();
        services.AddSingleton<IMonsterBehavior>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IMonsterBehaviorKey>(sp => sp.GetRequiredService<T>());
    }
}
