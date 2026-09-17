using System;

namespace Vortex.Primitives.Content;

/// <summary>Outcome of a content write, same shape as the other admin services.</summary>
public sealed record ContentAdminResult(bool Success, int? Id, string? ErrorCode)
{
    public static ContentAdminResult Ok(int id) => new(true, id, null);

    public static ContentAdminResult Fail(string errorCode) => new(false, null, errorCode);
}

/// <summary>
/// An achievement header. <paramref name="Name"/> is the identifier the client builds badge codes
/// from (<c>"ACH_" + Name + level</c>) and the key progression triggers look up, so renaming one
/// orphans both.
/// </summary>
public sealed record AchievementSpec(string Name, string Category, int DisplayMethod);

/// <summary>
/// One rung of an achievement. <paramref name="ProgressRequirement"/> is cumulative across levels,
/// and <paramref name="RewardType"/> follows the wallet rule: negative grants credits, otherwise it
/// is the activity-point currency type.
/// </summary>
public sealed record AchievementLevelSpec(
    int Level,
    string BadgeCode,
    int ProgressRequirement,
    int RewardAmount,
    int RewardType,
    int ScorePoints
);

/// <summary>What a pet gets out of a hand item. An id with no row is still held, just never eaten.</summary>
public sealed record HandItemSpec(int HandItemId, string Name, int Nutrition, int Thirst);

/// <summary>The editable half of a bot: what it is called and how it looks.</summary>
public sealed record BotSpec(string Name, string Motto, string Figure);

/// <summary>An NFT collection header.</summary>
public sealed record NftCollectionSpec(
    string CollectionCode,
    string Name,
    int BoostScore,
    int Status,
    string? RewardProductCode,
    string? BonusProductCode
);

/// <summary>One item of a collection. <paramref name="ProductCode"/> must name a real furniture
/// classname or the collection can never be completed.</summary>
public sealed record NftCollectionItemSpec(
    string ProductCode,
    string ItemTypeId,
    int ProductTypeId,
    int Score,
    string Rarity,
    int SortOrder
);

/// <summary>One offer on the Collectors Guild shop. <paramref name="ProductCode"/> must name a real
/// furniture classname: it is both what the buyer receives and how the client identifies the offer
/// when it buys.</summary>
public sealed record NftStoreOfferSpec(
    string ProductCode,
    int EmeraldPrice,
    bool IsFeatured,
    bool IsLimited,
    int MintLimit,
    string ItemTypeId,
    int ProductTypeId,
    int Score,
    string Rarity,
    bool Enabled,
    int SortOrder
);

/// <summary>
/// One kind of furniture players may convert into a Relic. <paramref name="ProductCode"/> must name
/// a real classname: the client counts the player's copies by the definition's sprite id, and a row
/// naming nothing is left out of the list entirely.
/// </summary>
/// <remarks>
/// The window is required rather than optional. The client disables the convert button once
/// <paramref name="EndsAt"/> has passed, so a row with no end is a row nobody can use.
/// </remarks>
public sealed record NftMintableItemTypeSpec(
    string ProductCode,
    int StampPrice,
    DateTime StartsAt,
    DateTime EndsAt,
    bool RegionLocked,
    bool LimitedEdition,
    int EditionSize,
    bool Enabled,
    int SortOrder
);

/// <summary>A bundle of stamps priced in silver. <paramref name="ProductCode"/> is a localization
/// key for the purchase dialog's title, not a furniture classname — nothing is delivered.</summary>
public sealed record NftMintTokenOfferSpec(
    string ProductCode,
    int SilverPrice,
    int AmountTokens,
    bool Enabled,
    int SortOrder
);

/// <summary>A Relic waiting for one player. <paramref name="SetId"/> is looked up by the client as
/// <c>collectibles.set.&lt;setId&gt;</c>, so a value with no localization entry shows raw.</summary>
public sealed record NftClaimSpec(
    int PlayerId,
    string ProductCode,
    string SetId,
    string DefaultCollectionName,
    string Collection,
    int ClaimLimit,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    int MinCollectorLevel
);

/// <summary>A currency the hotel offers.</summary>
public sealed record CurrencySpec(
    string Name,
    int CurrencyType,
    int? ActivityPointType,
    bool Enabled,
    int StartingAmount
);

/// <summary>One rung of the builders' club furni ladder.</summary>
public sealed record BuildersClubTierSpec(int Level, int FurniLimit);

/// <summary>The rental terms of one rentable space.</summary>
public sealed record RentableSpaceTermsSpec(
    int FurnitureId,
    int Price,
    int CurrencyTypeId,
    int RentDurationSeconds,
    bool RequiresHc
);

/// <summary>
/// An avatar a player can wear whole.
/// <para>
/// <paramref name="ContractKey"/> is not free text: the client switches on it for the caption and
/// the tile colours, and draws the word "null" for anything outside the three it knows —
/// <c>NftAvatarCollection</c> holds them.
/// </para>
/// <para>
/// <paramref name="EditionSize"/> of 0 means unlimited. It is the only thing enforcing scarcity;
/// there is no chain here that would.
/// </para>
/// </summary>
public sealed record NftAvatarSpec(
    string AvatarCode,
    string Name,
    string Figure,
    string Gender,
    string ContractKey,
    int EditionSize,
    bool Enabled,
    int SortOrder
);
