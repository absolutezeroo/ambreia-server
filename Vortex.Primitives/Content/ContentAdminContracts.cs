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
