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

    private static void Throw(ContentAdminResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorCode);
        }
    }
}
