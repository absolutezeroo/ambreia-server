using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Stats;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Skills.Configuration;
using Vortex.Rpg.Skills.Effects;
using Vortex.Rpg.Skills.Hits;
using Vortex.Rpg.Skills.Statuses;

namespace Vortex.Rpg.Skills;

/// <summary>Skills and status effects (spec M1b): the effect types it ships, each declared here and
/// nowhere else. A module that adds an effect declares its own line.</summary>
public sealed class SkillsModule : IHostPluginModule
{
    public string Key => "vortex-rpg-skills";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<SkillsConfig>(
            builder.Configuration.GetSection(SkillsConfig.SECTION_NAME)
        );

        AddEffect<DamageEffect>(services);
        AddEffect<HealEffect>(services);
        AddEffect<ApplyStatusEffect>(services);
        AddEffect<InterruptEffect>(services);
        AddEffect<DispelEffect>(services);
        AddEffect<ThreatEffect>(services);
        services.AddSingleton<SkillEffects>();

        // Statuses (spec M1b §7): one system, ticked by every zone and asked for its modifiers.
        services.AddSingleton<StatusApplier>();
        services.AddSingleton<StatusSystem>();
        services.AddSingleton<ICombatSystem>(sp => sp.GetRequiredService<StatusSystem>());
        services.AddSingleton<IStatModifierSource>(sp => sp.GetRequiredService<StatusSystem>());
        services.AddSingleton<IHitStep, DamageModifierStep>();
        services.AddSingleton<IHitStep, ShieldStep>();
    }

    /// <summary>One instance, seen both by the import (its rule) and by the zone (its effect).</summary>
    public static void AddEffect<T>(IServiceCollection services)
        where T : class, ISkillEffect
    {
        services.AddSingleton<T>();
        services.AddSingleton<ISkillEffectRule>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<ISkillEffect>(sp => sp.GetRequiredService<T>());
    }
}
