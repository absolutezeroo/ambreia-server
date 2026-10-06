using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Dashboard.API.Operations.Catalogue.Contracts;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Action;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Dashboard.API.Operations.Catalogue;

internal sealed class CurrencyOperations(OperationRunner runner, IGrainFactory grainFactory)
{
    private readonly OperationRunner _runner = runner;
    private readonly IGrainFactory _grainFactory = grainFactory;

    public Task<OperationResult> GiveCreditsAsync(
        GiveCreditsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.currency.credits.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { currency = "credits", request.Amount },
            work: c =>
                _grainFactory
                    .GetPlayerWalletGrain(new PlayerId(request.PlayerId))
                    .GrantCreditsAsync(request.Amount, c),
            ct
        );

    public Task<OperationResult> GiveActivityPointsAsync(
        GiveActivityPointsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.currency.activitypoints.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new
            {
                currency = "activity_points",
                request.Type,
                request.Amount,
            },
            work: c =>
                _grainFactory
                    .GetPlayerWalletGrain(new PlayerId(request.PlayerId))
                    .GrantActivityPointsAsync(request.Type, request.Amount, c),
            ct
        );

    /// <summary>
    /// Grants silver or emeralds — the two currencies that had no way in at all. They could be read
    /// and spent (the catalogue prices offers in silver) but never credited, so both stayed at zero
    /// for the life of an account.
    /// </summary>
    /// <param name="currency">Already validated by the endpoint, so this cannot be a currency the
    /// operation refuses to touch.</param>
    public Task<OperationResult> GiveCollectiblesCurrencyAsync(
        GiveCollectiblesCurrencyRequest request,
        CurrencyType currency,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.currency.collectibles.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { currency = currency.ToString(), request.Amount },
            work: async c =>
            {
                bool granted = await _grainFactory
                    .GetPlayerWalletGrain(new PlayerId(request.PlayerId))
                    .GrantCurrencyAsync(
                        new CurrencyKind { CurrencyType = currency },
                        request.Amount,
                        c
                    )
                    .ConfigureAwait(false);

                if (!granted)
                {
                    // Reported as a rejection rather than swallowed. A currency with no enabled
                    // currency_types row cannot be credited, and showing the operator a green
                    // result for it is how a grant that did nothing looks like one that worked.
                    throw new InvalidOperationException(
                        $"currency_not_configured: {currency} has no enabled currency_types row"
                    );
                }
            },
            ct
        );

    /// <summary>
    /// Only the two collectibles currencies. Credits and activity points are reachable through the
    /// same enum, but they have their own endpoints — routing them here would skip the
    /// activity-point type that one of them requires.
    /// </summary>
    internal static bool TryParseCollectiblesCurrency(string? value, out CurrencyType currency)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "silver":
                currency = CurrencyType.Silver;
                return true;
            case "emeralds":
                currency = CurrencyType.Emeralds;
                return true;
            default:
                currency = default;
                return false;
        }
    }

    public Task<OperationResult> GiveFurnitureAsync(
        GiveFurnitureRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.item.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.DefinitionId, request.ExtraData },
            work: c =>
                _grainFactory
                    .GetInventoryGrain(new PlayerId(request.PlayerId))
                    .GrantFurnitureDefinitionAsync(request.DefinitionId, request.ExtraData, c),
            ct
        );
}
