using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Primitives.Content;

namespace Vortex.Dashboard.API.Operations.Hotel;

/// <summary>
/// Content operations: achievement ladders, bots and hand items, NFT collections, the economy's
/// smaller tables, and direct player grants. Each routes through
/// <see cref="IContentAdminService"/> — never a direct DB write — which owns the live-cache reload
/// each domain needs, and each is audited with the operator's reason.
/// </summary>
internal sealed class ContentOperations(OperationRunner runner, IContentAdminService contentAdmin)
{
    private readonly OperationRunner _runner = runner;
    private readonly IContentAdminService _contentAdmin = contentAdmin;

    public Task<OperationResult> SaveAchievementAsync(
        AchievementRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            request.AchievementId > 0
                ? "ops.content.achievement.update"
                : "ops.content.achievement.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.AchievementId,
                request.Name,
                request.Category,
            },
            work: async c =>
            {
                AchievementSpec spec = new(request.Name, request.Category, request.DisplayMethod);

                Throw(
                    request.AchievementId > 0
                        ? await _contentAdmin
                            .UpdateAchievementAsync(request.AchievementId, spec, c)
                            .ConfigureAwait(false)
                        : await _contentAdmin.CreateAchievementAsync(spec, c).ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> DeleteAchievementAsync(
        DeleteAchievementRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.achievement.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.AchievementId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteAchievementAsync(request.AchievementId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SaveAchievementLevelAsync(
        AchievementLevelRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.achievement.level",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.AchievementId,
                request.Level,
                request.BadgeCode,
                request.ProgressRequirement,
            },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .UpsertAchievementLevelAsync(
                            request.AchievementId,
                            new AchievementLevelSpec(
                                request.Level,
                                request.BadgeCode,
                                request.ProgressRequirement,
                                request.RewardAmount,
                                request.RewardType,
                                request.ScorePoints
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteAchievementLevelAsync(
        DeleteAchievementLevelRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.achievement.level.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.LevelId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteAchievementLevelAsync(request.LevelId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SaveHandItemAsync(
        HandItemRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.hand_item.save",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.HandItemId, request.Name },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .UpsertHandItemAsync(
                            new HandItemSpec(
                                request.HandItemId,
                                request.Name,
                                request.Nutrition,
                                request.Thirst
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteHandItemAsync(
        DeleteHandItemRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.hand_item.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.Id },
            work: async c =>
                Throw(await _contentAdmin.DeleteHandItemAsync(request.Id, c).ConfigureAwait(false)),
            ct
        );

    /// <summary>
    /// The counterpart of the item grant, which had none.
    /// </summary>
    /// <remarks>
    /// Audited against the player who loses the item rather than the item id alone, so the trail
    /// reads as "took this from them" instead of "deleted row 40213".
    /// </remarks>
    public Task<OperationResult> RevokeFurnitureAsync(
        RevokeFurnitureRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.item.revoke",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.ItemId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .RevokeFurnitureAsync(request.PlayerId, request.ItemId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> TransferFurnitureAsync(
        TransferFurnitureRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.item.transfer",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.ItemId, request.ToPlayerId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .TransferFurnitureAsync(
                            request.PlayerId,
                            request.ToPlayerId,
                            request.ItemId,
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> RefundFurnitureAsync(
        RefundFurnitureRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.item.refund",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.ItemId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .RefundFurnitureAsync(request.PlayerId, request.ItemId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateBotAsync(
        BotRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.bot.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.BotId, request.Name },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .UpdateBotAsync(
                            request.BotId,
                            new BotSpec(request.Name, request.Motto, request.Figure),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteBotAsync(
        DeleteBotRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.bot.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.BotId },
            work: async c =>
                Throw(await _contentAdmin.DeleteBotAsync(request.BotId, c).ConfigureAwait(false)),
            ct
        );

    public Task<OperationResult> SaveCollectionAsync(
        CollectionRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            request.CollectionId > 0
                ? "ops.content.collection.update"
                : "ops.content.collection.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CollectionId, request.CollectionCode },
            work: async c =>
            {
                NftCollectionSpec spec = new(
                    request.CollectionCode,
                    request.Name,
                    request.BoostScore,
                    request.Status,
                    request.RewardProductCode,
                    request.BonusProductCode
                );

                Throw(
                    request.CollectionId > 0
                        ? await _contentAdmin
                            .UpdateCollectionAsync(request.CollectionId, spec, c)
                            .ConfigureAwait(false)
                        : await _contentAdmin.CreateCollectionAsync(spec, c).ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> SaveStoreOfferAsync(
        StoreOfferRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            request.OfferId > 0 ? "ops.content.storeoffer.update" : "ops.content.storeoffer.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.OfferId,
                request.ProductCode,
                request.EmeraldPrice,
            },
            work: async c =>
            {
                NftStoreOfferSpec spec = new(
                    request.ProductCode,
                    request.EmeraldPrice,
                    request.IsFeatured,
                    request.IsLimited,
                    request.MintLimit,
                    request.ItemTypeId,
                    request.ProductTypeId,
                    request.Score,
                    request.Rarity,
                    request.Enabled,
                    request.SortOrder
                );

                Throw(
                    request.OfferId > 0
                        ? await _contentAdmin
                            .UpdateStoreOfferAsync(request.OfferId, spec, c)
                            .ConfigureAwait(false)
                        : await _contentAdmin.CreateStoreOfferAsync(spec, c).ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> DeleteStoreOfferAsync(
        DeleteStoreOfferRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.storeoffer.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.OfferId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteStoreOfferAsync(request.OfferId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SaveMintableItemTypeAsync(
        MintableItemTypeRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            request.TypeId > 0
                ? "ops.content.mintabletype.update"
                : "ops.content.mintabletype.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.TypeId,
                request.ProductCode,
                request.StampPrice,
            },
            work: async c =>
            {
                NftMintableItemTypeSpec spec = new(
                    request.ProductCode,
                    request.StampPrice,
                    request.StartsAt,
                    request.EndsAt,
                    request.RegionLocked,
                    request.LimitedEdition,
                    request.EditionSize,
                    request.Enabled,
                    request.SortOrder
                );

                Throw(
                    request.TypeId > 0
                        ? await _contentAdmin
                            .UpdateMintableItemTypeAsync(request.TypeId, spec, c)
                            .ConfigureAwait(false)
                        : await _contentAdmin
                            .CreateMintableItemTypeAsync(spec, c)
                            .ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> DeleteMintableItemTypeAsync(
        DeleteMintableItemTypeRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.mintabletype.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TypeId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteMintableItemTypeAsync(request.TypeId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SaveMintTokenOfferAsync(
        MintTokenOfferRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            request.OfferId > 0 ? "ops.content.mintoffer.update" : "ops.content.mintoffer.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.OfferId,
                request.AmountTokens,
                request.SilverPrice,
            },
            work: async c =>
            {
                NftMintTokenOfferSpec spec = new(
                    request.ProductCode,
                    request.SilverPrice,
                    request.AmountTokens,
                    request.Enabled,
                    request.SortOrder
                );

                Throw(
                    request.OfferId > 0
                        ? await _contentAdmin
                            .UpdateMintTokenOfferAsync(request.OfferId, spec, c)
                            .ConfigureAwait(false)
                        : await _contentAdmin
                            .CreateMintTokenOfferAsync(spec, c)
                            .ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> DeleteMintTokenOfferAsync(
        DeleteMintTokenOfferRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.mintoffer.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.OfferId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteMintTokenOfferAsync(request.OfferId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SaveClaimAsync(
        ClaimRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.claim.create",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new
            {
                request.PlayerId,
                request.ProductCode,
                request.ClaimLimit,
                request.MinCollectorLevel,
            },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .CreateClaimAsync(
                            new NftClaimSpec(
                                request.PlayerId,
                                request.ProductCode,
                                request.SetId,
                                request.DefaultCollectionName,
                                request.Collection,
                                request.ClaimLimit,
                                request.ValidFrom,
                                request.ValidTo,
                                request.MinCollectorLevel
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteClaimAsync(
        DeleteClaimRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.claim.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.ClaimId },
            work: async c =>
                Throw(
                    await _contentAdmin.DeleteClaimAsync(request.ClaimId, c).ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteCollectionAsync(
        DeleteCollectionRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.collection.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CollectionId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteCollectionAsync(request.CollectionId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SaveCollectionItemAsync(
        CollectionItemRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            request.ItemId > 0
                ? "ops.content.collection.item.update"
                : "ops.content.collection.item.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.ItemId,
                request.CollectionId,
                request.ProductCode,
            },
            work: async c =>
            {
                NftCollectionItemSpec spec = new(
                    request.ProductCode,
                    request.ItemTypeId,
                    request.ProductTypeId,
                    request.Score,
                    request.Rarity,
                    request.SortOrder
                );

                Throw(
                    request.ItemId > 0
                        ? await _contentAdmin
                            .UpdateCollectionItemAsync(request.ItemId, spec, c)
                            .ConfigureAwait(false)
                        : await _contentAdmin
                            .CreateCollectionItemAsync(request.CollectionId, spec, c)
                            .ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> DeleteCollectionItemAsync(
        DeleteCollectionItemRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.collection.item.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.ItemId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteCollectionItemAsync(request.ItemId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SaveCurrencyAsync(
        CurrencyRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            request.CurrencyId > 0 ? "ops.content.currency.update" : "ops.content.currency.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CurrencyId, request.Name },
            work: async c =>
            {
                CurrencySpec spec = new(
                    request.Name,
                    request.CurrencyType,
                    request.ActivityPointType,
                    request.Enabled,
                    request.StartingAmount
                );

                Throw(
                    request.CurrencyId > 0
                        ? await _contentAdmin
                            .UpdateCurrencyAsync(request.CurrencyId, spec, c)
                            .ConfigureAwait(false)
                        : await _contentAdmin.CreateCurrencyAsync(spec, c).ConfigureAwait(false)
                );
            },
            ct
        );

    public Task<OperationResult> SaveBuildersClubTierAsync(
        BuildersClubTierRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.builders_club.save",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.Level, request.FurniLimit },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .UpsertBuildersClubTierAsync(
                            new BuildersClubTierSpec(request.Level, request.FurniLimit),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteBuildersClubTierAsync(
        DeleteBuildersClubTierRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.builders_club.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TierId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteBuildersClubTierAsync(request.TierId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> SaveRentableTermsAsync(
        RentableTermsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.rentable_terms.save",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.FurnitureId, request.Price },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .UpsertRentableSpaceTermsAsync(
                            new RentableSpaceTermsSpec(
                                request.FurnitureId,
                                request.Price,
                                request.CurrencyTypeId,
                                request.RentDurationSeconds,
                                request.RequiresHc
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteRentableTermsAsync(
        DeleteRentableTermsRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.rentable_terms.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TermsId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteRentableSpaceTermsAsync(request.TermsId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> CreateNftAvatarAsync(
        NftAvatarRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.nftavatar.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.AvatarCode,
                request.ContractKey,
                request.EditionSize,
            },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .CreateNftAvatarAsync(
                            new NftAvatarSpec(
                                request.AvatarCode,
                                request.Name,
                                request.Figure,
                                request.Gender,
                                request.ContractKey,
                                request.EditionSize,
                                request.Enabled,
                                request.SortOrder
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateNftAvatarAsync(
        UpdateNftAvatarRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.nftavatar.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.AvatarId,
                request.AvatarCode,
                request.EditionSize,
            },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .UpdateNftAvatarAsync(
                            request.AvatarId,
                            new NftAvatarSpec(
                                request.AvatarCode,
                                request.Name,
                                request.Figure,
                                request.Gender,
                                request.ContractKey,
                                request.EditionSize,
                                request.Enabled,
                                request.SortOrder
                            ),
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteNftAvatarAsync(
        DeleteNftAvatarRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.nftavatar.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.AvatarId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .DeleteNftAvatarAsync(request.AvatarId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> GrantNftAvatarAsync(
        NftAvatarGrantRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.nftavatar.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.AvatarId, request.Note },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .GrantNftAvatarAsync(request.AvatarId, request.PlayerId, request.Note, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> RevokeNftAvatarAsync(
        NftAvatarRevokeRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.nftavatar.revoke",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.CopyId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .RevokeNftAvatarAsync(request.CopyId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> GrantBadgeAsync(
        BadgeGrantRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.badge.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.BadgeCode },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .GrantBadgeAsync(request.PlayerId, request.BadgeCode, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> RevokeBadgeAsync(
        BadgeGrantRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.badge.revoke",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.BadgeCode },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .RevokeBadgeAsync(request.PlayerId, request.BadgeCode, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> GrantEffectAsync(
        EffectGrantRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.effect.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.EffectId, request.DurationSeconds },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .GrantEffectAsync(
                            request.PlayerId,
                            request.EffectId,
                            request.DurationSeconds,
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> RevokeEffectAsync(
        EffectGrantRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.effect.revoke",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.EffectId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .RevokeEffectAsync(request.PlayerId, request.EffectId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> GrantChatStyleAsync(
        ChatStyleGrantRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.chatstyle.grant",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.ClientStyleId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .GrantChatStyleAsync(request.PlayerId, request.ClientStyleId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> RevokeChatStyleAsync(
        ChatStyleGrantRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.content.chatstyle.revoke",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: null,
            detail: new { request.ClientStyleId },
            work: async c =>
                Throw(
                    await _contentAdmin
                        .RevokeChatStyleAsync(request.PlayerId, request.ClientStyleId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    private static void Throw(ContentAdminResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorCode);
        }
    }
}
