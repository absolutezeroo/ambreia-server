using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Quests;

/// <summary>
/// One VortexCoin reward owed to one player for one rung of one community goal, and — once it has
/// been paid — the transaction that paid it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the winners are written down instead of being worked out when the coins are sent.</b> The
/// rung's reward goes to the top <c>reward_user_limit</c> contributors, and contributions keep
/// arriving after the rung falls. Recomputing the list at payment time would quietly hand the
/// reward to whoever happened to be on top a few seconds later, so the standings are FROZEN into
/// these rows the moment the threshold is crossed. What the hall of fame showed when the bar filled
/// is what gets paid.
/// </para>
/// <para>
/// <b>Why it is a queue and not a chain call.</b> Minting is an Ethereum transaction: seconds, and
/// it can fail. The grain that detects the crossing owns the hotel's running total and is
/// single-threaded, so awaiting a mint in it would stall every other player's contribution behind
/// the chain. It writes rows; <c>CommunityGoalPayoutService</c> drains them. A failed mint is a row
/// that is still unpaid, and retried — not a reward that evaporated in a log line.
/// </para>
/// <para>
/// The address is stored rather than re-derived at payment time for the same reason the amount is:
/// this row is the receipt, and a receipt that recomputes its own figures is not one.
/// </para>
/// </remarks>
[Table("community_goal_payouts")]
// One reward per player per rung. This is the last line of defence behind
// CommunityGoalEntity.RewardedLevel: two nodes deciding the same rung fell at the same moment both
// build the same winner list, and the second insert is what has to fail rather than pay twice.
[Index(nameof(CommunityGoalEntityId), nameof(LevelNumber), nameof(PlayerEntityId), IsUnique = true)]
// The drain's own query: everything not yet paid, oldest first.
[Index(nameof(MintedAt))]
public class CommunityGoalPayoutEntity : VortexEntity
{
    [Column("goal_id")]
    public required int CommunityGoalEntityId { get; set; }

    /// <summary>The rung that was reached, matching <c>community_goal_levels.level_number</c>.</summary>
    [Column("level_number")]
    public required int LevelNumber { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    /// <summary>
    /// Where the player stood when the rung fell — 1 is the top contributor. The column is not
    /// called <c>rank</c>: MySQL 8 made RANK a reserved word for the window function, and while EF
    /// quotes every identifier it writes, the repair scripts in <c>scripts/sql/</c> are hand-written
    /// and would not.
    /// </summary>
    [Column("contributor_rank")]
    public required int Rank { get; set; }

    /// <summary>Their contribution at that moment, kept so the receipt explains itself.</summary>
    [Column("score")]
    public required int Score { get; set; }

    /// <summary>
    /// The hotel-held address the coins go to, resolved when the rung fell. Whole coins live in
    /// <see cref="AmountCoins" />; this is only where they land.
    /// </summary>
    [Column("address")]
    [MaxLength(64)]
    public required string Address { get; set; }

    /// <summary>Whole VortexCoin, copied off the rung. The mint multiplies by 10^18.</summary>
    [Column("amount_coins")]
    public required int AmountCoins { get; set; }

    /// <summary>When the mint succeeded; <c>null</c> while the reward is still owed.</summary>
    [Column("minted_at")]
    public DateTime? MintedAt { get; set; }

    /// <summary>The minting transaction, once there is one.</summary>
    [Column("tx_hash")]
    [MaxLength(80)]
    public string? TransactionHash { get; set; }

    /// <summary>
    /// How many times the drain has tried and failed. It is a counter and not a dead flag: a
    /// campaign whose payouts all sit at a rising number is a chain that is down, and that is worth
    /// seeing in the table rather than only in yesterday's logs.
    /// </summary>
    [Column("attempts")]
    [DefaultValue(0)]
    public int Attempts { get; set; }

    /// <summary>Why the last attempt failed, for the same reason.</summary>
    [Column("last_error")]
    [MaxLength(512)]
    public string? LastError { get; set; }

    [ForeignKey(nameof(CommunityGoalEntityId))]
    public CommunityGoalEntity? CommunityGoalEntity { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
