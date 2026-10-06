using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Catalogue;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Api.Hotel;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Api.Progression;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Api.Safety;
using Vortex.Dashboard.API.Api.Safety.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// Read-only surfaces for the domains that own tables but no admin flow: the social graph and guild
/// forums, the staff/role matrix, the economy's smaller tables (rentals, currencies), and what
/// players hold (badges/effects/chat styles/outfits).
/// <para>
/// Grouped in one mapper because none of them writes: every route here is a GET behind a read
/// capability, and the domains that do have operations keep their own file.
/// </para>
/// </summary>
internal static partial class DashboardEndpoints
{
    private const string TagSocial = "Social";
    private const string TagStaff = "Staff";
    private const string ApiSocial = ApiV1 + "/social";
    private const string ApiStaff = ApiV1 + "/staff";
    private const string ApiPlayerRewards = ApiV1 + "/player-rewards";

    public static void MapInsightReads(WebApplication app)
    {
        MapReadGet<SocialStats>(
            app,
            ApiSocial + "/stats",
            (HttpContext ctx, SocialReads social, CancellationToken ct) =>
                OkAsync(social.SocialStatsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.SocialRead,
            TagSocial
        );
        MapReadGet<StaffOverview>(
            app,
            ApiStaff,
            (StaffReads staff, CancellationToken ct) => OkAsync(staff.StaffAsync(ct)),
            Capabilities.Dashboard.StaffRead,
            TagStaff
        );
        MapReadGet<StaffAccountSearch>(
            app,
            ApiStaff + "/accounts",
            (HttpContext ctx, StaffReads staff, CancellationToken ct) =>
                OkAsync(staff.StaffAccountSearchAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.StaffRead,
            TagStaff
        );
        MapReadGet<EconomyExtras>(
            app,
            ApiEconomy + "/extras",
            (EconomyReads economy, CancellationToken ct) => OkAsync(economy.EconomyExtrasAsync(ct)),
            Capabilities.Dashboard.EconomyRead,
            TagEconomy
        );
        MapReadGet<PlayerRewardStats>(
            app,
            ApiPlayerRewards,
            (HttpContext ctx, PlayerRewardReads playerReward, CancellationToken ct) =>
                OkAsync(playerReward.PlayerRewardsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.PlayersRead,
            TagForensics
        );
        MapReadGetNullable<PlayerRewardDetail>(
            app,
            ApiPlayerRewards + "/{playerId:int}",
            (int playerId, PlayerRewardReads playerReward, CancellationToken ct) =>
                OkNullableAsync(playerReward.PlayerRewardDetailAsync(playerId, ct)),
            Capabilities.Dashboard.PlayersRead,
            TagForensics
        );
        MapReadGet<DashboardInventory>(
            app,
            ApiMonitoring + "/inventory",
            (InventoryReads inventory, CancellationToken ct) =>
                OkAsync(inventory.InventoryAsync(ct)),
            Capabilities.Dashboard.OverviewRead,
            TagMonitoring
        );
    }
}
