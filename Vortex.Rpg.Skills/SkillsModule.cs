using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Skills.Configuration;
using Vortex.Rpg.Skills.Effects;

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

        services.AddSingleton<ISkillEffectRule, DamageEffect>();
        services.AddSingleton<ISkillEffectRule, HealEffect>();
        services.AddSingleton<ISkillEffectRule, ApplyStatusEffect>();
        services.AddSingleton<ISkillEffectRule, InterruptEffect>();
        services.AddSingleton<ISkillEffectRule, DispelEffect>();
        services.AddSingleton<ISkillEffectRule, ThreatEffect>();
    }
}
