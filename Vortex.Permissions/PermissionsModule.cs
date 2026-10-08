using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Permissions.Configuration;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Plugins;

namespace Vortex.Permissions;

/// <summary>Who may do what: roles and capabilities.</summary>
public sealed class PermissionsModule : IHostPluginModule
{
    public string Key => "vortex-permissions";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<PermissionsConfig>(
            builder.Configuration.GetSection(PermissionsConfig.SECTION_NAME)
        );
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddHostedService<PermissionSeederService>();
    }
}
