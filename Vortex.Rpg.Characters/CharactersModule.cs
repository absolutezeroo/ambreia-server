using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Content;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Migrations;
using Vortex.Rpg.Stats;

namespace Vortex.Rpg.Characters;

/// <summary>Characters: the grain, its stat sources, its event handlers (spec Personnage).</summary>
public sealed class CharactersModule : IHostPluginModule
{
    public string Key => "vortex-rpg-characters";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<CharacterConfig>(
            builder.Configuration.GetSection(CharacterConfig.SECTION_NAME)
        );
        services.Configure<ItemConfig>(builder.Configuration.GetSection(ItemConfig.SECTION_NAME));

        // Declared, not scanned (plan Personnage, décision 1): a system that adds stats adds a line.
        services.AddSingleton<IStatSource, ClassLevelStatSource>();
        services.AddSingleton<IStatSource, EquipmentStatSource>();

        // Characters hold class ids: an import that removes one needs a replace rule.
        services.AddSingleton<IContentReferences, ClassReferences>();
        services.AddSingleton<IContentReferences, SpecializationReferences>();
        services.AddSingleton<IContentReferences, ItemReferences>();
    }
}
