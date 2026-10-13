using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Npcs;
using Vortex.Rpg.Quests.Npcs;
using Vortex.Rpg.Quests.Objectives;
using Vortex.Rpg.Quests.Rewards;
using Vortex.Rpg.Quests.Signals;
using Vortex.Rpg.Quests.Zones;
using Vortex.Rpg.Zones.Rules;

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
        services.AddSingleton<IContentKind, DialogueKind>();

        AddObjective<KillObjective>(services);
        AddObjective<ReachObjective>(services);
        AddObjective<InteractObjective>(services);
        AddObjective<TalkObjective>(services);
        AddObjective<CollectObjective>(services);

        AddReward<XpReward>(services);
        AddReward<ItemsReward>(services);

        services.AddSingleton<ZoneKeys>();

        // Spec M2a §3.3: the log's part of a zone's facts, the condition reading it, the action starting a quest.
        services.AddZoneFactsSource<QuestFactsSource>();
        services.AddZoneCondition<QuestStateEvaluator>();
        services.AddZoneAction<StartQuestExecutor>();

        // Spec M2a §3.3: what an NPC's dialogue says; the room keeps the NPC and the session.
        services.AddSingleton<INpcDialogues, QuestDialogues>();
    }

    private static void AddObjective<T>(IServiceCollection services)
        where T : class, IQuestObjectiveRule
    {
        services.AddSingleton<T>();
        services.AddSingleton<IQuestObjectiveRule>(sp => sp.GetRequiredService<T>());
    }

    private static void AddReward<T>(IServiceCollection services)
        where T : class, IQuestRewardKind
    {
        services.AddSingleton<T>();
        services.AddSingleton<IQuestRewardKind>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IQuestRewardRule>(sp => sp.GetRequiredService<T>());
    }
}
