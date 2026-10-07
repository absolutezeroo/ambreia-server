using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Vortex.Permissions.Moderation;
using Vortex.Permissions.WordFilter;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Plugins;
using WordFilterService = Vortex.Permissions.WordFilter.WordFilter;

namespace Vortex.Permissions;

/// <summary>
/// Who may do what: roles and capabilities, sanction presets, the call-for-help catalogue and the
/// hotel-wide word filter.
/// </summary>
public sealed class PermissionsModule : IHostPluginModule
{
    public string Key => "vortex-permissions";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<ISanctionPresetService, SanctionPresetService>();
        services.AddHostedService<PermissionSeederService>();
        services.AddHostedService<SanctionPresetSeederService>();
        // Must run after SanctionPresetSeederService: it links CFH topics to Ban presets by
        // (Kind, PresetIndex), which only exist once that seeder has run. Hosted services start in
        // registration order, so this ordering is load-bearing, not incidental.
        services.AddHostedService<CfhCatalogSeederService>();

        // The hotel-wide word filter. Its journal is the audit pipeline's; the loader performs the
        // first reload once the silo is up.
        services.TryAddSingleton<IWordFilter, WordFilterService>();
        services.AddHostedService<WordFilterLoader>();
    }
}
