using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Dashboard.API.Operations.Catalogue.Contracts;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Admin;
using Vortex.Primitives.Catalog.Enums;

namespace Vortex.Dashboard.API.Operations.Catalogue;

/// <summary>
/// Gift wrapping admin operations. Each routes through <see cref="IGiftWrappingAdminService" /> —
/// never a direct DB write — which reloads the live list after committing, so an added parcel is in
/// the next gift dialog without a restart.
/// </summary>
internal sealed class GiftWrappingOperations(
    OperationRunner runner,
    IGiftWrappingAdminService wrappingAdmin
)
{
    private readonly OperationRunner _runner = runner;
    private readonly IGiftWrappingAdminService _wrappingAdmin = wrappingAdmin;

    public Task<OperationResult> CreateGiftWrappingAsync(
        CreateGiftWrappingRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.giftwrapping.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.Kind,
                request.FurnitureName,
                request.ArtIndex,
                request.IsDefault,
                request.SortOrder,
            },
            work: async c =>
                Throw(
                    await _wrappingAdmin
                        .CreateAsync(
                            SpecOf(
                                request.Kind,
                                request.FurnitureName,
                                request.ArtIndex,
                                request.IsDefault,
                                request.SortOrder
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateGiftWrappingAsync(
        UpdateGiftWrappingRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.giftwrapping.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.WrappingId,
                request.Kind,
                request.FurnitureName,
                request.ArtIndex,
                request.IsDefault,
                request.SortOrder,
            },
            work: async c =>
                Throw(
                    await _wrappingAdmin
                        .UpdateAsync(
                            request.WrappingId,
                            SpecOf(
                                request.Kind,
                                request.FurnitureName,
                                request.ArtIndex,
                                request.IsDefault,
                                request.SortOrder
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteGiftWrappingAsync(
        DeleteGiftWrappingRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.giftwrapping.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.WrappingId },
            work: async c =>
                Throw(
                    await _wrappingAdmin.DeleteAsync(request.WrappingId, c).ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> ReloadGiftWrappingsAsync(
        ReloadGiftWrappingsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.giftwrapping.reload",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { },
            work: async c => Throw(await _wrappingAdmin.ReloadAsync(c).ConfigureAwait(false)),
            ct
        );

    /// <summary>The kind arrives as a string from the browser; anything outside the enum fails.</summary>
    private static GiftWrappingEntrySpec SpecOf(
        string kind,
        string? furnitureName,
        int? artIndex,
        bool isDefault,
        int sortOrder
    ) =>
        Enum.TryParse(kind, ignoreCase: true, out GiftWrappingKind parsed) && Enum.IsDefined(parsed)
            ? new GiftWrappingEntrySpec(parsed, furnitureName, artIndex, isDefault, sortOrder)
            : throw new InvalidOperationException("invalid_kind");

    private static void Throw(GiftWrappingAdminResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error);
        }
    }
}
