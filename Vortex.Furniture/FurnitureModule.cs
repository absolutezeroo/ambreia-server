using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Furniture.Configuration;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Plugins;

namespace Vortex.Furniture;

public sealed class FurnitureModule : IHostPluginModule
{
    public string Key => "turbo-furniture";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<FurnitureConfig>(
            builder.Configuration.GetSection(FurnitureConfig.SECTION_NAME)
        );

        services.AddSingleton<IFurnitureDefinitionProvider, FurnitureDefinitionProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<IFurnitureDefinitionProvider>()
        );
        services.AddSingleton<IStuffDataFactory, StuffDataFactory>();
    }
}
