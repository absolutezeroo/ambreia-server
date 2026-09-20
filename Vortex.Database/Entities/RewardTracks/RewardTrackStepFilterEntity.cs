using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.RewardTracks;

namespace Vortex.Database.Entities.RewardTracks;

/// <summary>
/// One node of a step's condition tree: a test, or one of the joins that hold tests together.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> is either a literal or <c>$N</c>, naming an earlier step: that is what
/// "the same furniture" is made of, and it is the whole reason these hang off a step rather than
/// off the task.
/// </para>
/// <para>
/// The table was a flat list that ANDed, and the tree is that reading generalised rather than
/// replacing it: a row with no <see cref="ParentId"/> and the default <see cref="Kind"/> is exactly
/// what every row already is. Kept as rows rather than a document so each node keeps its own audit
/// trail, which is what the rest of this schema does with content somebody edits.
/// </para>
/// </remarks>
[Table("reward_track_step_filters")]
[Index(nameof(RewardTrackTaskStepEntityId))]
[Index(nameof(ParentId))]
public class RewardTrackStepFilterEntity : VortexEntity
{
    [Column("step_id")]
    public required int RewardTrackTaskStepEntityId { get; set; }

    /// <summary>
    /// What this row is: a comparison, or a node that joins other rows.
    /// </summary>
    /// <remarks>
    /// <see cref="PredicateKind.Test"/> is zero, so every row written before this column existed is
    /// a test — which is what they all were.
    /// </remarks>
    [Column("kind")]
    [DefaultValue(PredicateKind.Test)]
    public PredicateKind Kind { get; set; } = PredicateKind.Test;

    /// <summary>
    /// The joining row this one sits under, or null for a child of the step's implicit root.
    /// </summary>
    /// <remarks>
    /// Null on every row written before this column existed, which reads as the flat list they
    /// formed: the children of a root that joins them with <see cref="PredicateKind.All"/>. That is
    /// precisely what "a step's filters are ANDed" meant, so the migration moves no data.
    /// </remarks>
    [Column("parent_id")]
    public int? ParentId { get; set; }

    /// <summary>
    /// Order among the rows sharing a parent. Presentation only under an <c>All</c>, where it
    /// cannot change the answer; under an <c>Any</c> it decides which branch is tried first, which
    /// cannot change the answer either because a branch has no side effect.
    /// </summary>
    [Column("sort_order")]
    [DefaultValue(0)]
    public int SortOrder { get; set; }

    /// <summary>One of <see cref="RewardTrackFacts"/>.</summary>
    [Column("fact_key")]
    [MaxLength(ContentIdLength)]
    public required string FactKey { get; set; }

    [Column("operator")]
    [DefaultValue(StepFilterOperator.Equals)]
    public required StepFilterOperator Operator { get; set; }

    [Column("value")]
    [MaxLength(512)]
    [DefaultValue("")]
    public string Value { get; set; } = string.Empty;

    [ForeignKey(nameof(RewardTrackTaskStepEntityId))]
    public RewardTrackTaskStepEntity? Step { get; set; }
}
