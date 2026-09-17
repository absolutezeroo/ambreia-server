using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.RewardTracks;

/// <summary>
/// One prize, claimed once, by one player. The unique index is the claim's idempotency: a second
/// attempt loses the insert and is answered from the row that is already there.
/// </summary>
/// <remarks>
/// The insert and the grant do <em>not</em> share a transaction, whatever this used to say. The row
/// commits first and the bundle goes out after it, which is the right order — the entitlement is
/// owed from the moment it is spent — but it only works if the claim carries the operation the
/// bundle is owed under. See <see cref="OperationId"/>.
/// </remarks>
[Table("player_reward_track_claims")]
[Index(nameof(PlayerEntityId), nameof(TrackId), nameof(PrizeId), IsUnique = true)]
[Index(nameof(TrackId), nameof(PrizeId))]
public class PlayerRewardTrackClaimEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("track_id")]
    [MaxLength(ContentIdLength)]
    public required string TrackId { get; set; }

    [Column("prize_id")]
    [MaxLength(ContentIdLength)]
    public required string PrizeId { get; set; }

    [Column("claimed_at")]
    public required DateTime ClaimedAt { get; set; }

    /// <summary>Points the player held when they claimed. What made the claim legal, kept.</summary>
    [Column("points_at_claim")]
    [DefaultValue(0)]
    public int PointsAtClaim { get; set; }

    /// <summary>
    /// The operation the bundle is owed under, written in the same insert as the claim.
    /// </summary>
    /// <remarks>
    /// Without it the id was minted fresh for each attempt and recorded nowhere. A stop between the
    /// claim committing and the bundle going out therefore left the prize spent, the operation
    /// sitting at Prepared where the post-pivot sweep does not look, and the next click answered
    /// AlreadyClaimed — the reward gone with nothing able to finish it. On the row, that next click
    /// is the retry: every granter is idempotent under its operation, so one already delivered
    /// hands over nothing twice.
    ///
    /// Empty on rows written before this column existed. Those predate any operation and are left
    /// alone rather than guessed at.
    /// </remarks>
    [Column("operation_id")]
    public Guid OperationId { get; set; }

    /// <summary>
    /// What was actually handed over, rendered at claim time — <c>currency:0x100</c>,
    /// <c>badge:ACH_Foo</c>. The prize definition can be rewritten afterwards; this cannot, so
    /// "why does this player have that?" stays answerable a year later.
    /// </summary>
    [Column("granted_summary")]
    [DefaultValue("")]
    public string GrantedSummary { get; set; } = string.Empty;

    /// <summary>
    /// The claim spent its entitlement and the bundle never finished going out — the one state a
    /// later click is allowed to resume.
    /// </summary>
    /// <remarks>
    /// Both halves matter, in both directions. Resuming a claim that <em>did</em> deliver would ask
    /// the granters to hand the bundle over again, and they only refuse because of their receipts;
    /// leaning on that rather than on this would make a correct outcome depend on a second
    /// mechanism. Refusing to resume one that did not deliver is the loss this exists to end.
    /// A claim with no operation predates the column, has no operation to resume under, and is left
    /// alone rather than guessed at.
    /// </remarks>
    [NotMapped]
    public bool IsDeliveryUnfinished => OperationId != Guid.Empty && GrantedSummary.Length == 0;

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
