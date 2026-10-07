using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Catalog.Providers;
using Vortex.Database.Context;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Catalog.Tags;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Plugins;

namespace Vortex.Catalog;

public sealed class CatalogModule : IHostPluginModule
{
    public string Key => "vortex-catalog";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<ICatalogService, CatalogService>();

        services.AddSingleton<IMembershipOfferProvider, MembershipOfferProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<IMembershipOfferProvider>()
        );
        services.AddSingleton<IMembershipGiftProvider, MembershipGiftProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<IMembershipGiftProvider>()
        );
        services.AddSingleton<IGiftWrappingProvider, GiftWrappingProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<IGiftWrappingProvider>()
        );
        services.AddSingleton<ICatalogSnapshotProvider<NormalCatalog>>(
            sp => new CatalogSnapshotProvider<NormalCatalog>(
                sp.GetRequiredService<IDbContextFactory<VortexDbContext>>(),
                sp.GetRequiredService<ILogger<ICatalogSnapshotProvider<NormalCatalog>>>(),
                sp.GetRequiredService<IFurnitureDefinitionProvider>(),
                CatalogType.Normal
            )
        );
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<ICatalogSnapshotProvider<NormalCatalog>>()
        );
        // The admin services moved to the dashboard: authoring content is its job, not the
        // emulator's. A host without that module has no authoring path here, deliberately.
    }
}
