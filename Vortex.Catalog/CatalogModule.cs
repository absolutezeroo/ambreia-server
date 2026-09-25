using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Catalog.Providers;
using Vortex.Catalog.Vouchers;
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

        // Singleton because the counts must outlive a packet: the handler is built fresh per
        // invocation, so anything it held would reset on every attempt and count nothing (SEC-11).
        services.AddSingleton<IVoucherAttemptLimiter, VoucherAttemptLimiter>();
        services.AddSingleton<ILtdScheduleService, LtdScheduleService>();
        services.AddSingleton<ICatalogClubOfferProvider, CatalogClubOfferProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<ICatalogClubOfferProvider>()
        );
        services.AddSingleton<ICatalogClubGiftProvider, CatalogClubGiftProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<ICatalogClubGiftProvider>()
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
        services.AddSingleton<ICatalogSnapshotProvider<BuildersClubCatalog>>(
            sp => new CatalogSnapshotProvider<BuildersClubCatalog>(
                sp.GetRequiredService<IDbContextFactory<VortexDbContext>>(),
                sp.GetRequiredService<ILogger<ICatalogSnapshotProvider<BuildersClubCatalog>>>(),
                sp.GetRequiredService<IFurnitureDefinitionProvider>(),
                CatalogType.BuildersClub
            )
        );
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)
                sp.GetRequiredService<ICatalogSnapshotProvider<BuildersClubCatalog>>()
        );
        // The admin services moved to the dashboard: authoring content is its job, not the
        // emulator's. A host without that module has no authoring path here, deliberately.
    }
}
