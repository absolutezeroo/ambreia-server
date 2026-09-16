using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Players;

/// <summary>
/// A self-custody wallet a player has proved they control — the addresses the game client reads
/// AFTER the Stardust one.
/// </summary>
/// <remarks>
/// <para>
/// Only proved ones get here. The link endpoint issues a nonce, the wallet signs it, and the
/// address recovered from that signature is what is written — never an address somebody typed. This
/// table is therefore a record of possession and not of intent.
/// </para>
/// <para>
/// The Stardust wallet is NOT here. It is derived from the hotel's master seed on demand
/// (<c>HotelWallets</c>), so storing it would be storing a value that can always be recomputed and
/// that would go stale the day the seed changed.
/// </para>
/// </remarks>
[Table("player_wallets")]
[Index(nameof(PlayerEntityId))]
// One address, one player, and this is the line that makes a contest countable: without it a player
// links the same wallet from ten accounts and every balance is counted ten times.
[Index(nameof(Address), IsUnique = true)]
public class PlayerWalletEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    /// <summary>
    /// EIP-55 checksummed, as <c>EthereumSignature.RecoverAddress</c> answers it. Stored in that
    /// form because it is what every explorer and wallet displays; comparisons are made
    /// case-insensitively, since a wallet may hand over the lowercase.
    /// </summary>
    [Column("address")]
    [MaxLength(42)]
    public required string Address { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
