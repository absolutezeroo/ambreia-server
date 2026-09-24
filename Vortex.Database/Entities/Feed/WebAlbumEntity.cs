using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Feed;

/// <summary>
/// A named collection of one player's published photos, added to over time.
/// </summary>
/// <remarks>
/// An album lives and dies with its card in the feed (<see cref="FeedPostEntityId"/>): its likes,
/// shares and comments are the card's, and deleting the card — by its owner or by staff — deletes
/// the album. That is what keeps one delete path, one second-factor gate and one takedown for both.
/// </remarks>
[Table("web_albums")]
[Index(nameof(PlayerEntityId), nameof(CreatedAt))]
[Index(nameof(FeedPostEntityId), IsUnique = true)]
public class WebAlbumEntity : VortexEntity
{
    public const int TitleLength = 60;

    public const int DescriptionLength = 500;

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("title")]
    [MaxLength(TitleLength)]
    public required string Title { get; set; }

    [Column("description")]
    [MaxLength(DescriptionLength)]
    public required string Description { get; set; }

    /// <summary>The album's card in the feed, which comes back up when photos are added.</summary>
    [Column("feed_post_id")]
    public required int FeedPostEntityId { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [ForeignKey(nameof(FeedPostEntityId))]
    public WebFeedPostEntity? FeedPostEntity { get; set; }

    public ICollection<WebAlbumPhotoEntity> Photos { get; set; } = [];
}
