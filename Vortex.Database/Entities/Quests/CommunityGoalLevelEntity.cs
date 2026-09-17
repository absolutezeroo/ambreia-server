using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Quests;

/// <summary>
/// One rung of a community goal: the total score that unlocks it, and how many contributors are
/// rewarded at that rung. The client reads the reward limits as a flat array in level order, which
/// is why the level number matters as much as the threshold.
/// </summary>
[Table("community_goal_levels")]
[Index(nameof(CommunityGoalEntityId), nameof(LevelNumber), IsUnique = true)]
public class CommunityGoalLevelEntity : VortexEntity
{
    [Column("goal_id")]
    public required int CommunityGoalEntityId { get; set; }

    /// <summary>1-based rung, in ascending threshold order.</summary>
    [Column("level_number")]
    public required int LevelNumber { get; set; }

    /// <summary>Community total needed to reach this rung.</summary>
    [Column("score_threshold")]
    public required int ScoreThreshold { get; set; }

    /// <summary>How many top contributors are rewarded when this rung is reached.</summary>
    [Column("reward_user_limit")]
    [DefaultValue(0)]
    public int RewardUserLimit { get; set; }

    /// <summary>
    /// Whole VortexCoin paid to each of those top contributors when this rung falls. Zero — the
    /// default, and what every rung authored before this column existed carries — means the rung
    /// rewards nothing on the chain, which is how a goal that is not a coin campaign behaves.
    /// </summary>
    /// <remarks>
    /// Whole coins, not wei: an operator types "20" into the dashboard and the payout multiplies by
    /// 10^18 on its way to the contract. A wei column would make the smallest useful reward a
    /// nineteen-digit number in a form field.
    /// <para>
    /// This number times <see cref="RewardUserLimit" />, summed over the ladder, IS the campaign's
    /// whole emission — and it is knowable before the campaign opens, which is the reason the coin
    /// reward hangs off the rung and not off each contribution. A faucet that pays per action cannot
    /// answer "how much will this mint" until it is over.
    /// </para>
    /// </remarks>
    [Column("reward_coins")]
    [DefaultValue(0)]
    public int RewardCoins { get; set; }

    [ForeignKey(nameof(CommunityGoalEntityId))]
    public CommunityGoalEntity? CommunityGoalEntity { get; set; }
}
