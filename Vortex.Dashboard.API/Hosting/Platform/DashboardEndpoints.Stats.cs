using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Catalogue;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Api.Hotel;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Api.Safety;
using Vortex.Dashboard.API.Api.Safety.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>Read-only analytics endpoints for domains that previously had zero dashboard visibility
/// (guilds, pets, CFH, catalog purchases) — see the individual
/// each subject's <c>*Reads</c> class for what it aggregates and why.</summary>
internal static partial class DashboardEndpoints
{
    private const string TagStats = "Stats";
    private const string ApiGroups = ApiV1 + "/groups";
    private const string ApiPets = ApiV1 + "/pets";
    private const string ApiCfh = ApiV1 + "/cfh";

    public static void MapStatsReads(WebApplication app)
    {
        MapReadGet<GroupStats>(
            app,
            ApiGroups + "/stats",
            (HttpContext ctx, GroupReads groups, CancellationToken ct) =>
                OkAsync(groups.GroupsStatsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.GroupsRead,
            TagStats
        );
        MapReadGet<PetStats>(
            app,
            ApiPets + "/stats",
            (HttpContext ctx, PetReads pets, CancellationToken ct) =>
                OkAsync(pets.PetsStatsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.PetsRead,
            TagStats
        );
        MapReadGet<CfhStats>(
            app,
            ApiCfh + "/stats",
            (HttpContext ctx, CfhReads cfh, CancellationToken ct) =>
                OkAsync(cfh.CfhStatsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.CfhRead,
            TagStats
        );
        MapReadGet<CatalogPurchaseStats>(
            app,
            ApiCatalog + "/purchases/stats",
            (HttpContext ctx, CatalogPurchaseReads catalogPurchase, CancellationToken ct) =>
                OkAsync(catalogPurchase.CatalogPurchasesStatsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.CatalogPurchasesRead,
            TagStats
        );
    }
}
