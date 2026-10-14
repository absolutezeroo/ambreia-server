using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.Rpg.Content.Authoring;
using Vortex.Rpg.Chat;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Authoring;
using Vortex.Rpg.Content.Import;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Registry;

namespace Vortex.Rpg;

/// <summary>The RPG content pipeline: its kinds, the registry every system reads, the import.</summary>
public sealed class RpgModule : IHostPluginModule
{
    public string Key => "vortex-rpg";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        // Declared, not scanned (spec Contenu §4.3): a system that brings a kind adds its line.
        services.AddSingleton<IContentKind, ClassKind>();
        services.AddSingleton<IContentKind, SpecializationKind>();
        services.AddSingleton<IContentKind, ProgressionKind>();
        services.AddSingleton<IContentKind, ItemKind>();
        services.AddSingleton<IContentKind, MonsterKind>();
        // Its effects are checked by the rules the skills module declares (spec M1b §5.4).
        services.AddSingleton<IContentKind, SkillKind>();
        services.AddSingleton<IContentKind, StatusKind>();

        // A chat link to an item (spec F3c §6), checked against this content.
        services.AddSingleton<IChatLinkKind, ItemChatLinkKind>();

        // One instance behind both: the reloader loads the very registry the systems read.
        services.AddSingleton<RpgContentRegistry>();
        services.AddSingleton<IRpgContent>(sp => sp.GetRequiredService<RpgContentRegistry>());
        services.AddSingleton<IReferenceDataProvider>(sp =>
            sp.GetRequiredService<RpgContentRegistry>()
        );
        services.AddSingleton<IRpgContentService, RpgContentService>();
        services.AddSingleton<StoredPackReader>();
        services.AddSingleton<IContentAuthoring, ContentAuthoringService>();
    }
}
