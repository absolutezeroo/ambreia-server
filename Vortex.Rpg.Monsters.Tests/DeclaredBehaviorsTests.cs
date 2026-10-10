using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Monsters.Behaviors;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1c §5.4: a behavior a module declares in one plain line is one the content import
/// accepts — no second registration to forget.</summary>
public sealed class DeclaredBehaviorsTests
{
    private sealed class Flying : IMonsterBehavior
    {
        public string Key => "flying";

        public void Think(MonsterBrain brain) { }
    }

    [Fact]
    public void ABehaviorDeclaredByAnotherModule_IsAKeyTheImportAccepts()
    {
        ServiceCollection services = new();
        new MonstersModule().ConfigureServices(services, Host.CreateApplicationBuilder());
        services.AddSingleton<IMonsterBehavior, Flying>();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<IMonsterBehaviorKeys>()
            .Keys.Should()
            .BeEquivalentTo(["melee", "ranged", "caster", "flying"]);
    }
}
