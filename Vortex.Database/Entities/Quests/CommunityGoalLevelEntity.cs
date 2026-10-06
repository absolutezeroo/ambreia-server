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

    [ForeignKey(nameof(CommunityGoalEntityId))]
    public CommunityGoalEntity? CommunityGoalEntity { get; set; }
}
