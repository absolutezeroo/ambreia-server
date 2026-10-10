using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones.Rules;

/// <summary>
/// How a module declares a zone rule or a part of the zone's facts (spec Z1 §4.2–4.3): one
/// instance, seen by the rooms as the piece that answers and by the editor's validator as a rule
/// that has an effect. Declaring the same piece twice declares it once.
/// </summary>
public static class ZoneRuleServices
{
    public static IServiceCollection AddZoneCondition<T>(this IServiceCollection services)
        where T : class, IZoneConditionEvaluator
    {
        if (Declared<T>(services))
        {
            return services;
        }

        services.AddSingleton<T>();
        services.AddSingleton<IZoneConditionEvaluator>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IZoneRuleDeclaration>(sp => sp.GetRequiredService<T>());

        return services;
    }

    public static IServiceCollection AddZoneAction<T>(this IServiceCollection services)
        where T : class, IZoneActionExecutor
    {
        if (Declared<T>(services))
        {
            return services;
        }

        services.AddSingleton<T>();
        services.AddSingleton<IZoneActionExecutor>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IZoneRuleDeclaration>(sp => sp.GetRequiredService<T>());

        return services;
    }

    public static IServiceCollection AddZoneFactsSource<T>(this IServiceCollection services)
        where T : class, IZoneFactsSource
    {
        if (Declared<T>(services))
        {
            return services;
        }

        services.AddSingleton<T>();
        services.AddSingleton<IZoneFactsSource>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IZoneTriggerSource>(sp => sp.GetRequiredService<T>());

        return services;
    }

    /// <summary>Trigger types the module raises from elsewhere than its facts (spec Z1 §9).</summary>
    public static IServiceCollection AddZoneTrigger(
        this IServiceCollection services,
        params ZoneTriggerType[] types
    ) => services.AddSingleton<IZoneTriggerSource>(new DeclaredZoneTriggers(types));

    private static bool Declared<T>(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(T));
}
