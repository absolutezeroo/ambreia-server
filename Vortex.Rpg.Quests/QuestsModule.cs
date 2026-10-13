using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Quests.Objectives;

namespace Vortex.Rpg.Quests;

/// <summary>
/// Quests (spec M2a): the content kind, the quest log grain, the kinds it ships. A module that brings
/// an objective or a reward kind declares one line, as here; the kind is both its content rule and
/// its runtime.
/// </summary>
public sealed class QuestsModule : IHostPluginModule
{
    public string Key => "vortex-rpg-quests";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<IContentKind, QuestKind>();

        AddObjective<KillObjective>(services);
        AddObjective<ReachObjective>(services);
        AddObjective<InteractObjective>(services);
        AddObjective<TalkObjective>(services);
        AddObjective<CollectObjective>(services);
    }

    private static void AddObjective<T>(IServiceCollection services)
        where T : class, IQuestObjectiveRule
    {
        services.AddSingleton<T>();
        services.AddSingleton<IQuestObjectiveRule>(sp => sp.GetRequiredService<T>());
    }
}
