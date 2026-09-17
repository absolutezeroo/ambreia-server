using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Polls;
using Vortex.Primitives.Prizes;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Signals;
using Vortex.Progression.Achievements.Events;
using Vortex.Progression.Configuration;
using Vortex.Progression.Polls;
using Vortex.Progression.Providers;
using Vortex.Progression.Quests;
using Vortex.Progression.Quests.Events;

namespace Vortex.Progression;

/// <summary>
/// What the progression module registers on its own behalf: the achievement batching window, the
/// account level ladder cache, and the four admin services the dashboard writes progression content
/// through.
/// </summary>
public sealed class ProgressionModule : IHostPluginModule
{
    public string Key => "vortex-progression";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<AchievementConfig>(
            builder.Configuration.GetSection(AchievementConfig.SECTION_NAME)
        );

        services.AddSingleton<AccountLevelProvider>();
        services.AddSingleton<IAccountLevelProvider>(sp =>
            sp.GetRequiredService<AccountLevelProvider>()
        );
        services.AddSingleton<IReferenceDataProvider>(sp =>
            sp.GetRequiredService<AccountLevelProvider>()
        );

        // The interest gate for the three signal consumers. Singletons rather than the consumers
        // themselves, because handlers are not services: the feature processor builds one per
        // invocation, so there would be nothing for the gate to hold. Each publishes the keys of its
        // own mapping table, so the gate cannot drift from what the consumer actually handles.
        services.AddSingleton<ISignalInterestSource, AchievementSignalInterest>();
        services.AddSingleton<ISignalInterestSource, DailyTaskSignalInterest>();
        services.AddSingleton<ISignalInterestSource, QuestSignalInterest>();

        // The community goal's coin rewards. Unlike the leaderboard beside it there is no second
        // registration keeping one instance in two roles — nothing reads this one, it only drains
        // community_goal_payouts. It returns immediately on a hotel with no chain.
        services.AddHostedService<CommunityGoalPayoutService>();

        // The admin services moved to the dashboard: authoring content is its job, not the
        // emulator's. A host without that module has no authoring path here, deliberately.
    }
}
