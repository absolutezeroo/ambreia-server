using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Habbicons;
using Vortex.Primitives.Habbicons.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Snapshots;

namespace Vortex.RewardTracks.Rewards;

/// <summary>
/// The granters, one per reward kind.
/// </summary>
/// <remarks>
/// Grouped in one file because each is a handful of lines that forwards to a service the hotel
/// already has — the same reason the quest module keeps its dozen event handlers together. They are
/// still separate classes with separate registrations, so adding a kind adds a class here and
/// changes nothing else.
/// </remarks>
internal static class RewardGranterHelpers
{
    /// <summary>
    /// Parses a reward's type id as a number. Returns false for content that names a furniture or
    /// an effect with something that is not one, which the pipeline reports rather than throwing.
    /// </summary>
    public static bool TryParseId(string rewardTypeId, out int id) =>
        int.TryParse(rewardTypeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
}

/// <summary>Currency: credits, duckets, diamonds, or anything else with a <c>currency_types</c> row.</summary>
/// <remarks>
/// <c>RewardTypeId</c> is the activity-point type, in the client's own numbering: -1 is credits and
/// everything else is an activity-point currency. That is exactly what the client's purse widget
/// reads it as, so the same number drives the icon and the grant.
/// </remarks>
internal sealed class CurrencyRewardGranter(
    IGrainFactory grainFactory,
    ILogger<CurrencyRewardGranter> logger
) : IRewardGranter
{
    private const int CreditsActivityPointType = -1;

    public RewardKind Kind => RewardKind.Currency;

    public async Task<bool> GrantAsync(
        int playerId,
        RewardGrantSnapshot reward,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    )
    {
        if (
            reward.Amount <= 0
            || !RewardGranterHelpers.TryParseId(reward.RewardTypeId, out int type)
        )
        {
            return false;
        }

        CurrencyKind kind =
            type == CreditsActivityPointType
                ? new CurrencyKind { CurrencyType = CurrencyType.Credits }
                : new CurrencyKind
                {
                    CurrencyType = CurrencyType.ActivityPoints,
                    ActivityPointType = type,
                };

        // Once, under the operation's own step: a retried claim finds the receipt and does not pay
        // twice, which a plain grant call could not promise.
        bool credited = await grainFactory
            .GetPlayerWalletGrain(playerId)
            .CreditOnceAsync(
                [new WalletDebitRequest { CurrencyKind = kind, Amount = reward.Amount }],
                operation,
                stepKey,
                ct
            )
            .ConfigureAwait(false);

        if (!credited)
        {
            // Almost always a missing currency_types row: the wallet reports it rather than
            // silently doing nothing, and this is the layer that has to say so out loud.
            logger.LogWarning(
                "Reward currency type {Type} could not be credited to player {PlayerId}; is there a currency_types row for it?",
                type,
                playerId
            );
        }

        return credited;
    }
}

/// <summary>A badge. <c>RewardTypeId</c> is the badge code.</summary>
internal sealed class BadgeRewardGranter(IGrainFactory grainFactory) : IRewardGranter
{
    public RewardKind Kind => RewardKind.Badge;

    public async Task<bool> GrantAsync(
        int playerId,
        RewardGrantSnapshot reward,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(reward.RewardTypeId))
        {
            return false;
        }

        // Badge grants are already idempotent: the inventory refuses a duplicate, so a retry is a
        // no-op without needing a receipt of its own.
        await grainFactory
            .GetInventoryGrain(playerId)
            .GrantBadgeAsync(reward.RewardTypeId, ct)
            .ConfigureAwait(false);

        return true;
    }
}

/// <summary>Furniture. Both floor and wall items are the same grant — the definition knows which it is.</summary>
internal sealed class FurnitureRewardGranter(IGrainFactory grainFactory) : IRewardGranter
{
    public RewardKind Kind => RewardKind.FloorItem;

    public async Task<bool> GrantAsync(
        int playerId,
        RewardGrantSnapshot reward,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    )
    {
        if (!RewardGranterHelpers.TryParseId(reward.RewardTypeId, out int definitionId))
        {
            return false;
        }

        // Every copy in one commit, under one receipt. Granting n copies with n calls is how a
        // failure halfway leaves a player with three of five and a refund for all five.
        await grainFactory
            .GetInventoryGrain(playerId)
            .GrantFurnitureDefinitionCopiesAsync(
                definitionId,
                string.IsNullOrEmpty(reward.ExtraParams) ? null : reward.ExtraParams,
                Math.Max(1, reward.Amount),
                operation,
                stepKey,
                ct
            )
            .ConfigureAwait(false);

        return true;
    }
}

/// <summary>
/// A wall item. Identical work to <see cref="FurnitureRewardGranter"/> — the furniture definition
/// already says whether it hangs on a wall — but a separate kind so the client draws the right
/// preview, which is the only thing the distinction is for.
/// </summary>
internal sealed class WallItemRewardGranter(IGrainFactory grainFactory) : IRewardGranter
{
    private readonly FurnitureRewardGranter _inner = new(grainFactory);

    public RewardKind Kind => RewardKind.WallItem;

    public Task<bool> GrantAsync(
        int playerId,
        RewardGrantSnapshot reward,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    ) => _inner.GrantAsync(playerId, reward, operation, stepKey, ct);
}

/// <summary>An avatar effect. <c>RewardTypeId</c> is the effect id, <c>Amount</c> its duration in seconds.</summary>
internal sealed class AvatarEffectRewardGranter(IGrainFactory grainFactory) : IRewardGranter
{
    public RewardKind Kind => RewardKind.AvatarEffect;

    public async Task<bool> GrantAsync(
        int playerId,
        RewardGrantSnapshot reward,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    )
    {
        if (!RewardGranterHelpers.TryParseId(reward.RewardTypeId, out int effectId))
        {
            return false;
        }

        // Sub-type 0 and a duration of 0, which the effect grain reads as permanent. A reward-track
        // effect that expired would be a strange thing to have earned.
        await grainFactory
            .GetPlayerEffectGrain(playerId)
            .AddEffectAsync(effectId, 0, Math.Max(0, reward.Amount), ct)
            .ConfigureAwait(false);

        return true;
    }
}

/// <summary>A Habbicon. The integration point between the two systems, and the whole of it.</summary>
/// <remarks>
/// The reward track knows a Habbicon id and nothing else about Habbicons; the Habbicon grain knows
/// nothing about reward tracks beyond a value on its acquisition-source enum. Neither could name a
/// type from the other's domain if it wanted to.
/// </remarks>
internal sealed class HabbiconRewardGranter(IGrainFactory grainFactory) : IRewardGranter
{
    public RewardKind Kind => RewardKind.Habbicon;

    public async Task<bool> GrantAsync(
        int playerId,
        RewardGrantSnapshot reward,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    )
    {
        if (!RewardGranterHelpers.TryParseId(reward.RewardTypeId, out int habbiconId))
        {
            return false;
        }

        HabbiconGrantResult result = await grainFactory
            .GetPlayerHabbiconGrain(playerId)
            .GrantAsync(habbiconId, HabbiconSource.RewardTrack, ct)
            .ConfigureAwait(false);

        // Idempotent on the Habbicon side already: a second grant of one the player owns succeeds
        // and reports it was not new, so a retried claim needs no receipt here either.
        return result.Succeeded;
    }
}

/// <summary>A chat-bubble style. <c>RewardTypeId</c> is the id the client's own style library uses.</summary>
/// <remarks>
/// Only ids 1000..9999 can be handed over, and the grain is where that rule lives: below 1000 the
/// client gates the style itself (free, HC, ambassador, staff) and never asks the server, and from
/// 10000 up it reads a different message that nothing composes yet. A content row naming an id
/// outside that band returns false — the content's fault, not a transient one — so the pipeline
/// records it as refused rather than owed.
/// </remarks>
internal sealed class ChatStyleRewardGranter(IGrainFactory grainFactory) : IRewardGranter
{
    public RewardKind Kind => RewardKind.ChatStyle;

    public async Task<bool> GrantAsync(
        int playerId,
        RewardGrantSnapshot reward,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    )
    {
        if (!RewardGranterHelpers.TryParseId(reward.RewardTypeId, out int chatStyleId))
        {
            return false;
        }

        // Idempotent in the grain: a style already held reports success, so a retried claim is a
        // no-op and needs no receipt of its own.
        return await grainFactory
            .GetPlayerGrain(playerId)
            .GrantChatStyleAsync(chatStyleId, ct)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// An entitlement: a capability rather than an object. <c>RewardTypeId</c> is a perk code such as
/// <c>TRADE</c> — the trading pass the Introduction Track hands out.
/// </summary>
/// <remarks>
/// Deliberately routed through the hotel's existing <see cref="PlayerPerkFlags"/> rather than a
/// reward-track-shaped entitlement of its own. That is what stops reward tracks from growing a
/// dependency on the trading code: the track sets a flag, and trading reads the flag it already
/// read.
/// </remarks>
internal sealed class EntitlementRewardGranter(
    IGrainFactory grainFactory,
    ILogger<EntitlementRewardGranter> logger
) : IRewardGranter
{
    public RewardKind Kind => RewardKind.Entitlement;

    public async Task<bool> GrantAsync(
        int playerId,
        RewardGrantSnapshot reward,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    )
    {
        PlayerPerkFlags? perk = PlayerPerkExtensions.FromLegacyString(reward.RewardTypeId);

        if (perk is null)
        {
            logger.LogWarning(
                "Reward entitlement '{Entitlement}' is not a known perk code; player {PlayerId} was granted nothing.",
                reward.RewardTypeId,
                playerId
            );

            return false;
        }

        await grainFactory
            .GetPlayerGrain(PlayerId.Parse(playerId))
            .GrantPerkAsync(perk.Value, ct)
            .ConfigureAwait(false);

        return true;
    }
}
