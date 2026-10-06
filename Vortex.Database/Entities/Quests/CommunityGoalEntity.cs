using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Quests;

/// <summary>
/// A hotel-wide goal everyone contributes to, shown on the landing view. One is active at a time:
/// the enabled goal with the lowest sort order whose window has not closed.
/// </summary>
[Table("community_goals")]
[Index(nameof(Code), IsUnique = true)]
public class CommunityGoalEntity : VortexEntity
{
    /// <summary>Sent to the client as <c>goalCode</c>; also the localization key it renders with.</summary>
    [Column("code")]
    public required string Code { get; set; }

    /// <summary>
    /// Quests of this campaign feed the goal: completing one contributes
    /// <see cref="ScorePerQuest"/>. Empty means nothing feeds it automatically.
    /// </summary>
    [Column("campaign_code")]
    [DefaultValue("")]
    public string CampaignCode { get; set; } = string.Empty;

    [Column("score_per_quest")]
    [DefaultValue(1)]
    public int ScorePerQuest { get; set; } = 1;

    [Column("enabled")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    /// <summary>When the goal stops accepting contributions; null = no deadline.</summary>
    [Column("ends_at")]
    public DateTime? EndsAt { get; set; }

    [Column("sort_order")]
    [DefaultValue(0)]
    public int SortOrder { get; set; }
}
