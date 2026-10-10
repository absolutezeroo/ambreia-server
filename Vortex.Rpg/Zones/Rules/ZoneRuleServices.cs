using Microsoft.Extensions.DependencyInjection;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones.Rules;

/// <summary>
/// How a module declares a zone rule (spec Z1 §4.2): one instance, seen by the rooms as the piece
/// that answers and by the editor's validator as a rule that has an effect.
/// </summary>
public static class ZoneRuleServices
{
    public static IServiceCollection AddZoneCondition<T>(this IServiceCollection services)
        where T : class, IZoneConditionEvaluator
    {
        services.AddSingleton<T>();
        services.AddSingleton<IZoneConditionEvaluator>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IZoneRuleDeclaration>(sp => sp.GetRequiredService<T>());

        return services;
    }

    public static IServiceCollection AddZoneAction<T>(this IServiceCollection services)
        where T : class, IZoneActionExecutor
    {
        services.AddSingleton<T>();
        services.AddSingleton<IZoneActionExecutor>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IZoneRuleDeclaration>(sp => sp.GetRequiredService<T>());

        return services;
    }
}
