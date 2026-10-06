using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Groups;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;

namespace Vortex.Database.Entities.Feed;

/// <summary>
/// One entry in the website's social feed — and one comment on one, because they are the same row.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ParentEntityId"/> null is a post; set makes the row a comment on the post it names.
/// That is the load-bearing decision in this whole feature: the screenshot it was built from shows
/// a comment form carrying the same three tabs as the post form — text, photos, poll — so a comment
/// has to be able to hold everything a post holds. With one table it already does, and photos,
/// poll choices, votes, reactions and tags all attach to a comment through the rows they attach to
/// a post with. With two tables every one of those needs either a second table or a polymorphic
/// owner column.
/// </para>
/// <para>
/// <see cref="PlayerEntityId"/> is the real author and stays the real author even when
/// <see cref="GroupEntityId"/> makes the post display as a group. A post published under a group
/// is still something a person did, and the row is the only place that survives them leaving the
/// group.
/// </para>
/// <para>
/// The thread is one level deep by construction: the service refuses a parent that itself has a
/// parent. The screenshot has no nested replies, and a flat thread is one query rather than a
/// walk.
/// </para>
/// </remarks>
[Table("web_feed_posts")]
[Index(nameof(ParentEntityId), nameof(CreatedAt))]
[Index(nameof(PlayerEntityId), nameof(CreatedAt))]
[Index(nameof(DeletedAt), nameof(CreatedAt))]
public class WebFeedPostEntity : VortexEntity
{
    /// <summary>The author, always the person — never the group they published as.</summary>
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    /// <summary>The identity shown on the card, when the author chose one of their groups.</summary>
    [Column("group_id")]
    public int? GroupEntityId { get; set; }

    /// <summary>Null on a post; the post being commented on otherwise.</summary>
    [Column("parent_id")]
    public int? ParentEntityId { get; set; }

    [Column("kind")]
    public required FeedPostKind Kind { get; set; }

    [Column("body")]
    [MaxLength(BodyLength)]
    public required string Body { get; set; }

    /// <summary>
    /// What the composer accepts. Long enough for the status the screenshot shows and short enough
    /// that the column stays comfortably inside an index-bearing row.
    /// </summary>
    public const int BodyLength = 500;

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [ForeignKey(nameof(GroupEntityId))]
    public GroupEntity? GroupEntity { get; set; }

    [ForeignKey(nameof(ParentEntityId))]
    public WebFeedPostEntity? ParentEntity { get; set; }

    public ICollection<WebFeedPostEntity> Children { get; set; } = [];

    public ICollection<WebFeedPollChoiceEntity> PollChoices { get; set; } = [];

    public ICollection<WebFeedReactionEntity> Reactions { get; set; } = [];

    public ICollection<WebFeedPostTagEntity> Tags { get; set; } = [];
}
