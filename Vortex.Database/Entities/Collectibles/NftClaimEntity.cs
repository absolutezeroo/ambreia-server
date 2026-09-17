using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Collectibles;

/// <summary>
/// A prize waiting for one player to collect from the Collectors Guild's Rewards tab.
/// </summary>
/// <remarks>
/// Habbo calls these Relics, and on the real hotel they are tokens minted elsewhere. Here a claim is
/// simply "this player may take this piece of furniture, this many times" — no chain involved, the
/// same way a collection is a list of classnames and a prize.
/// </remarks>
[Table("nft_claims")]
[Index(nameof(PlayerEntityId), nameof(ClaimCode), IsUnique = true)]
public class NftClaimEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    /// <summary>Identifies the claim to the client. It is echoed back on a claim, though the client's
    /// only button claims everything at once.</summary>
    [Column("claim_code")]
    [MaxLength(64)]
    public required string ClaimCode { get; set; }

    /// <summary>The furniture classname handed over.</summary>
    [Column("product_code")]
    [MaxLength(128)]
    public required string ProductCode { get; set; }

    /// <summary>
    /// What the client shows as the collection this reward belongs to. It looks the label up as
    /// <c>collectibles.set.&lt;setId&gt;</c> in its localization, so a value with no entry there is
    /// displayed raw.
    /// </summary>
    [Column("set_id")]
    [MaxLength(128)]
    public string SetId { get; set; } = string.Empty;

    /// <summary>The fallback name carried beside the set id; the client reads it but does not show
    /// it on this tab.</summary>
    [Column("default_collection_name")]
    [MaxLength(128)]
    public string DefaultCollectionName { get; set; } = string.Empty;

    /// <summary>The collection field on the claim itself, distinct from the item's set id.</summary>
    [Column("collection")]
    [MaxLength(128)]
    public string Collection { get; set; } = string.Empty;

    /// <summary>How many times this may be taken. The tab lists a claim only while the player has
    /// taken it fewer times than this, and shows the remainder as the quantity.</summary>
    [Column("claim_limit")]
    public int ClaimLimit { get; set; } = 1;

    [Column("claimed_amount")]
    public int ClaimedAmount { get; set; }

    /// <summary>
    /// The collector level the player must have reached before this claim is offered to them. 0
    /// offers it to everybody, which is what every claim written before this column existed does.
    /// </summary>
    /// <remarks>
    /// This is how Habbo gates its own monthly rewards: <c>collectibles.no_claims</c> promises
    /// prizes "based on your collector level", and the hotel view advertises them to "Collector
    /// Levels of 5+". A level here is the one we send in <c>NftCollectionsScore</c> — the number of
    /// collections the player has finished — so the scale an operator types against is the same one
    /// the client prints in the Collectors Guild header.
    /// </remarks>
    [Column("min_collector_level")]
    public int MinCollectorLevel { get; set; }

    [Column("valid_from")]
    public DateTime? ValidFrom { get; set; }

    /// <summary>Shown as the expiry date. Null means it never expires, which the client draws as the
    /// epoch — so a hotel that wants no date should set one far out rather than leave it empty.</summary>
    [Column("valid_to")]
    public DateTime? ValidTo { get; set; }

    [Column("status")]
    public int Status { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? Player { get; set; }
}
