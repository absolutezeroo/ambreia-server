using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Dashboard.API.Operations.Progression.Contracts;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Loot;
using Vortex.Primitives.Loot.Admin;

namespace Vortex.Dashboard.API.Operations.Progression;

/// <summary>
/// Loot table admin operations. Each routes through
/// <see cref="Vortex.Primitives.Loot.ILootTableAdminService"/> (never a direct DB write), which
/// reloads the live pool cache after committing, and emits a durable audit event with the operator's
/// reason — same contract as the catalog/quest operations.
/// </summary>
internal sealed class LootTableOperations(
    OperationRunner runner,
    ILootTableAdminService lootTableAdmin
)
{
    private readonly OperationRunner _runner = runner;
    private readonly ILootTableAdminService _lootTableAdmin = lootTableAdmin;

    public Task<OperationResult> CreateLootTableAsync(
        CreateLootTableRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.Code,
                request.Name,
                request.Variants,
            },
            work: async c =>
                Throw(
                    await _lootTableAdmin
                        .CreatePoolAsync(
                            new LootTableSpec(
                                request.Code,
                                request.Name,
                                request.Variants,
                                request.Notes,
                                request.Enabled
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateLootTableAsync(
        UpdateLootTableRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.PoolId,
                request.Code,
                request.Enabled,
            },
            work: async c =>
                Throw(
                    await _lootTableAdmin
                        .UpdatePoolAsync(
                            request.PoolId,
                            new LootTableSpec(
                                request.Code,
                                request.Name,
                                request.Variants,
                                request.Notes,
                                request.Enabled
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteLootTableAsync(
        DeleteLootTableRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.PoolId },
            work: async c =>
                Throw(
                    await _lootTableAdmin.DeletePoolAsync(request.PoolId, c).ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> CreateLootEntryAsync(
        CreateLootEntryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.entry.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.PoolCode,
                request.Variant,
                request.ProductType,
                request.Weight,
            },
            work: async c =>
            {
                if (!TryParseProductType(request.ProductType, out ProductType productType))
                {
                    throw new InvalidOperationException("invalid_request");
                }

                Throw(
                    await _lootTableAdmin
                        .CreateEntryAsync(
                            new LootEntrySpec(
                                request.PoolCode,
                                request.Variant,
                                productType,
                                request.FurnitureDefinitionId,
                                request.ExtraParam,
                                request.Weight,
                                request.Enabled
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> UpdateLootEntryAsync(
        UpdateLootEntryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.entry.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.EntryId,
                request.PoolCode,
                request.Weight,
            },
            work: async c =>
            {
                if (!TryParseProductType(request.ProductType, out ProductType productType))
                {
                    throw new InvalidOperationException("invalid_request");
                }

                Throw(
                    await _lootTableAdmin
                        .UpdateEntryAsync(
                            request.EntryId,
                            new LootEntrySpec(
                                request.PoolCode,
                                request.Variant,
                                productType,
                                request.FurnitureDefinitionId,
                                request.ExtraParam,
                                request.Weight,
                                request.Enabled
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> DeleteLootEntryAsync(
        DeleteLootEntryRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.entry.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.EntryId },
            work: async c =>
                Throw(
                    await _lootTableAdmin.DeleteEntryAsync(request.EntryId, c).ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> ReloadLootTablesAsync(
        ReloadLootTablesRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.reload",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { },
            work: async c => Throw(await _lootTableAdmin.ReloadCacheAsync(c).ConfigureAwait(false)),
            ct
        );

    public Task<OperationResult> CreateLootBindingAsync(
        CreateLootBindingRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.binding.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.FurnitureDefinitionId,
                request.PoolCode,
                request.HitsRequired,
            },
            work: async c =>
                Throw(
                    await _lootTableAdmin
                        .CreateBindingAsync(
                            new LootBindingSpec(
                                request.FurnitureDefinitionId,
                                request.PoolCode,
                                request.HitsRequired,
                                request.Enabled
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateLootBindingAsync(
        UpdateLootBindingRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.binding.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.BindingId,
                request.PoolCode,
                request.HitsRequired,
            },
            work: async c =>
                Throw(
                    await _lootTableAdmin
                        .UpdateBindingAsync(
                            request.BindingId,
                            new LootBindingSpec(
                                request.FurnitureDefinitionId,
                                request.PoolCode,
                                request.HitsRequired,
                                request.Enabled
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteLootBindingAsync(
        DeleteLootBindingRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.loottable.binding.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.BindingId },
            work: async c =>
                Throw(
                    await _lootTableAdmin
                        .DeleteBindingAsync(request.BindingId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    /// <summary>The product type arrives as a string from the browser; a value outside the enum must
    /// fail the request rather than default to Floor and quietly grant the wrong kind of prize. The
    /// pool travels as its code and is resolved (and rejected when unknown) by the admin service.</summary>
    private static bool TryParseProductType(string productType, out ProductType parsed) =>
        Enum.TryParse(productType, ignoreCase: true, out parsed);

    private static void Throw(LootAdminResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorCode);
        }
    }
}
