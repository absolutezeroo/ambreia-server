using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Dashboard.API.Api;
using Vortex.Dashboard.API.Api.Catalogue;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Dashboard.API.Operations;
using Vortex.Dashboard.API.Operations.Catalogue;
using Vortex.Dashboard.API.Operations.Catalogue.Contracts;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Dashboard.API.Security;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players.Enums.Wallet;

namespace Vortex.Dashboard.API.Hosting;

internal static partial class DashboardEndpoints
{
    public static void MapEconomyReads(WebApplication app)
    {
        MapReadGet<EconomyLedgerPage>(
            app,
            ApiEconomy + "/ledger",
            (HttpContext ctx, EconomyReads reads, CancellationToken ct) =>
                OkAsync(reads.EconomyAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.EconomyRead,
            TagEconomy
        );
        MapReadGet<ClubSubscriptions>(
            app,
            ApiEconomy + "/subscriptions",
            (HttpContext ctx, EconomyReads reads, CancellationToken ct) =>
                OkAsync(reads.ClubSubscriptionsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.EconomyRead,
            TagEconomy
        );
        MapReadGet<EconomyTrends>(
            app,
            ApiEconomy + "/trends",
            (HttpContext ctx, EconomyReads reads, CancellationToken ct) =>
                OkAsync(reads.EconomyTrendsAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.EconomyRead,
            TagEconomy
        );
        MapReadGet<MarketplaceSummary>(
            app,
            ApiEconomy + "/marketplace",
            (HttpContext ctx, EconomyReads reads, CancellationToken ct) =>
                OkAsync(reads.MarketplaceSummaryAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.EconomyRead,
            TagEconomy
        );
        MapReadGet<RentableSpaceAuditPage>(
            app,
            ApiV1 + "/rentable-spaces/activity",
            (HttpContext ctx, EconomyReads reads, CancellationToken ct) =>
                OkAsync(reads.RentableSpacesAsync(ctx.QueryAsNameValues(), ct)),
            Capabilities.Dashboard.EconomyRead,
            TagEconomy
        );
    }

    public static void MapCurrencyOperations(WebApplication app)
    {
        MapPost(
            app,
            ApiOperations + "/currency/credits",
            async (
                HttpContext ctx,
                GiveCreditsRequest body,
                CurrencyOperations currencyOps,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0 || body.Amount <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await currencyOps
                        .GiveCreditsAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsGrantCurrency,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/currency/activity-points",
            async (
                HttpContext ctx,
                GiveActivityPointsRequest body,
                CurrencyOperations currencyOps,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0 || body.Type < 0 || body.Amount <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await currencyOps
                        .GiveActivityPointsAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsGrantCurrency,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/currency/collectibles",
            async (
                HttpContext ctx,
                GiveCollectiblesCurrencyRequest body,
                CurrencyOperations currencyOps,
                CancellationToken ct
            ) =>
            {
                if (
                    body.PlayerId <= 0
                    || body.Amount <= 0
                    || !CurrencyOperations.TryParseCollectiblesCurrency(
                        body.Currency,
                        out CurrencyType currency
                    )
                )
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await currencyOps
                        .GiveCollectiblesCurrencyAsync(body, currency, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsGrantCurrency,
            TagOperations
        );

        MapPost(
            app,
            ApiOperations + "/items/grant",
            async (
                HttpContext ctx,
                GiveFurnitureRequest body,
                CurrencyOperations currencyOps,
                CancellationToken ct
            ) =>
            {
                if (body.PlayerId <= 0 || body.DefinitionId <= 0)
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await currencyOps
                        .GiveFurnitureAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsGrantItem,
            TagOperations
        );
        // Same capability as the grant, which is the line badges and effects already draw: whoever
        // may hand an item out may take it back. A separate one would mean an operator who can
        // create a mistake but not undo it.
        MapPost(
            app,
            ApiOperations + "/items/revoke",
            async (
                HttpContext ctx,
                RevokeFurnitureRequest body,
                ContentOperations contentOps,
                CancellationToken ct
            ) =>
                body.PlayerId <= 0 || body.ItemId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await contentOps
                            .RevokeFurnitureAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsGrantItem,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/items/transfer",
            async (
                HttpContext ctx,
                TransferFurnitureRequest body,
                ContentOperations contentOps,
                CancellationToken ct
            ) =>
                body.PlayerId <= 0 || body.ToPlayerId <= 0 || body.ItemId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await contentOps
                            .TransferFurnitureAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsGrantItem,
            TagOperations
        );
        // A refund moves money, so it answers to the currency capability as well as the item one --
        // MapPost takes a single policy, and paying out is the more dangerous half.
        MapPost(
            app,
            ApiOperations + "/items/refund",
            async (
                HttpContext ctx,
                RefundFurnitureRequest body,
                ContentOperations contentOps,
                CancellationToken ct
            ) =>
                body.PlayerId <= 0 || body.ItemId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await contentOps
                            .RefundFurnitureAsync(body, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsGrantCurrency,
            TagOperations
        );
    }

    public static void MapVoucherOperations(WebApplication app)
    {
        MapPost(
            app,
            ApiOperations + "/vouchers",
            async (
                HttpContext ctx,
                CreateVoucherRequest body,
                VouchersOperations vouchers,
                CancellationToken ct
            ) =>
            {
                if (
                    string.IsNullOrWhiteSpace(body.Code)
                    || body.Amount <= 0
                    || body.CurrencyType is < 1 or > 4
                )
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await vouchers
                        .CreateVoucherAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsManageVouchers,
            TagOperations
        );
        MapPost(
            app,
            ApiOperations + "/vouchers/deactivate",
            async (
                HttpContext ctx,
                DeactivateVoucherRequest body,
                VouchersOperations vouchers,
                CancellationToken ct
            ) =>
            {
                if (string.IsNullOrWhiteSpace(body.Code))
                {
                    return Results.BadRequest(new { error = "invalid_request" });
                }

                return Results.Ok(
                    await vouchers
                        .DeactivateVoucherAsync(body, ctx.ActorEmail(), ct)
                        .ConfigureAwait(false)
                );
            },
            Capabilities.Dashboard.OpsManageVouchers,
            TagOperations
        );
        MapReadGet<VoucherSnapshot>(
            app,
            ApiOperations + "/vouchers/{code}",
            async (string code, VouchersOperations vouchers, CancellationToken ct) =>
                Results.Ok(await vouchers.GetVoucherSnapshotAsync(code, ct).ConfigureAwait(false)),
            Capabilities.Dashboard.OpsManageVouchers,
            TagOperations
        );
    }
}
