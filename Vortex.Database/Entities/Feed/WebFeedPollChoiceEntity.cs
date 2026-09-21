using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Feed;

/// <summary>One answer a poll offers.</summary>
[Table("web_feed_poll_choices")]
[Index(nameof(WebFeedPostEntityId), nameof(Ordinal), IsUnique = true)]
public class WebFeedPollChoiceEntity : VortexEntity
{
    [Column("post_id")]
    public required int WebFeedPostEntityId { get; set; }

    [Column("ordinal")]
    public required int Ordinal { get; set; }

    [Column("label")]
    [MaxLength(LabelLength)]
    public required string Label { get; set; }

    public const int LabelLength = 80;

    [ForeignKey(nameof(WebFeedPostEntityId))]
    public WebFeedPostEntity? WebFeedPostEntity { get; set; }

    public ICollection<WebFeedPollVoteEntity> Votes { get; set; } = [];
}
