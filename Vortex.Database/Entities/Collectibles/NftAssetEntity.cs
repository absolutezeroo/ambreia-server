using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Collectibles;

/// <summary>
/// A Relic: one piece of furniture a player converted, and what they hold in its place.
/// </summary>
/// <remarks>
/// <para>
/// The furniture row is gone by the time this one exists — that is what conversion means, and it is
/// where the badge text "Converted N items to Relics" comes from. So this is the only remaining
/// record of the item, which is why it keeps the classname itself rather than relying on a
/// definition that an admin may later delete.
/// </para>
/// <para>
/// It is also counted towards collections. Otherwise minting a collectible would lower the score of
/// the collection it belongs to, and the Collectors Guild would be punishing collecting.
/// </para>
/// </remarks>
[Table("nft_assets")]
[Index(nameof(PlayerEntityId))]
// The edition is a database invariant, not a checked-then-hoped one: two players converting the
// last copy of the same classname at the same moment both count the same total, and the second
// insert is what has to fail. Its mint is refunded rather than the edition oversold.
[Index(nameof(ProductCode), nameof(SerialNumber), IsUnique = true)]
// Reconciling a wallet against the chain looks its Relics up by address.
[Index(nameof(WalletAddress))]
public class NftAssetEntity : VortexEntity
{
    /// <summary>
    /// Who holds it IN THE HOTEL — and, once <see cref="WalletAddress" /> is set, who held it last.
    /// It is not cleared on the way out, because the ledger reads better with it and because a Relic
    /// that comes home usually comes home to the same person.
    ///
    /// <para>
    /// <b>It is therefore not, on its own, a claim of ownership.</b> A Relic out on the chain still
    /// names its last hotel owner here, and that owner may well have sold it to a stranger. Every
    /// query that means "what does this player own" pairs this with
    /// <c>WalletAddress == null</c>; see the remarks on that property for the two that deliberately
    /// do not.
    /// </para>
    /// </summary>
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    /// <summary>
    /// The chain address this Relic was transferred out to, or <c>null</c> while it is in the hotel.
    /// Non-null means it is <b>gone from the game</b>: absent from the inventory tab, un-tradeable,
    /// and counting towards nobody's collection score. The player owns it in their wallet, where the
    /// hotel has no say over it.
    ///
    /// <para>
    /// One column and not a state enum, because there is no third state and because the address is
    /// needed anyway — it is what a claim back into the hotel has to be checked against.
    /// </para>
    ///
    /// <para>
    /// <b>Two queries deliberately ignore this, and must keep ignoring it:</b> the edition count in
    /// <c>PlayerMintGrain</c> and the sold-out list in <c>NftMintingGrain</c>. Those count how many
    /// of a classname EXIST, not who has them — filter them and a player could take a Relic out of
    /// the hotel to free a slot and mint another, turning "1 of 5" into a lie. They read by
    /// <c>ProductCode</c> with no owner condition, which is already right; the risk is somebody
    /// later adding the filter for consistency.
    /// </para>
    /// </summary>
    [Column("wallet_address")]
    [MaxLength(42)]
    public string? WalletAddress { get; set; }

    /// <summary>The classname of the furniture that was converted.</summary>
    [Column("product_code")]
    [MaxLength(128)]
    public required string ProductCode { get; set; }

    /// <summary>
    /// The definition it came from, kept for the sprite the client draws it with. Nullable because
    /// an asset outlives its definition: the classname above is what identifies the Relic.
    /// </summary>
    [Column("furniture_definition_id")]
    public int? FurnitureDefinitionEntityId { get; set; }

    /// <summary>The inventory id of the furniture that was consumed. Audit only — that row no
    /// longer exists.</summary>
    [Column("source_item_id")]
    public int SourceItemId { get; set; }

    /// <summary>What the conversion cost, as it was priced on the day.</summary>
    [Column("stamp_cost")]
    public int StampCost { get; set; }

    /// <summary>
    /// Which one of its edition this is: 1 for the first ever converted of this classname, counting
    /// up. Assigned at the mint and never changed — a Relic keeps its number through every trade,
    /// which is the whole point of having one.
    /// </summary>
    [Column("serial_number")]
    public int SerialNumber { get; set; }

    /// <summary>
    /// How many of this classname may ever exist, copied from the mintable type at the moment of
    /// the mint. Zero is an open edition. It is copied rather than looked up so that lowering a cap
    /// later cannot make an already-minted Relic read as "#7 of 5".
    /// </summary>
    [Column("edition_size")]
    public int EditionSize { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [ForeignKey(nameof(FurnitureDefinitionEntityId))]
    public FurnitureDefinitionEntity? FurnitureDefinitionEntity { get; set; }
}
