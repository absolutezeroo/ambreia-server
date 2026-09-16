using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;

namespace Vortex.Primitives.Collectibles.Grains;

/// <summary>
/// One player's stamps and Relics: buying stamps, spending them on a conversion, and what the
/// conversion left behind.
/// </summary>
/// <remarks>
/// Keyed by player for the same reason the claims grain is: a conversion destroys a piece of
/// furniture and spends a balance, so two clicks on the confirm button must not run at once. A
/// player-keyed grain settles that without putting the whole hotel behind one lock.
/// </remarks>
public interface IPlayerMintGrain : IGrainWithIntegerKey
{
    /// <summary>How many stamps the player holds.</summary>
    public Task<int> GetTokenBalanceAsync(CancellationToken ct);

    /// <summary>
    /// Buys a stamp bundle with silver. Returns the balance afterwards, unchanged if the purchase
    /// was refused — the client re-reads the balance either way, so it always gets the truth.
    /// </summary>
    public Task<MintTokenPurchaseResult> PurchaseTokensAsync(int offerId, CancellationToken ct);

    /// <summary>
    /// Converts one item the player owns into a Relic: checks the terms, spends the stamps,
    /// destroys the furniture and records the asset.
    /// </summary>
    public Task<MintOutcome> MintAsync(int itemId, CancellationToken ct);

    /// <summary>The Relics the player holds, as the inventory's Collectibles tab lists them.</summary>
    public Task<ImmutableArray<CollectibleAssetSnapshot>> GetAssetsAsync(CancellationToken ct);

    /// <summary>
    /// The self-custody wallet addresses this player has proved on the website.
    /// </summary>
    /// <remarks>
    /// Not a mint concern, and here anyway: the packet-handler assembly references primitives and no
    /// database, so a grain is the only way it can read a table — and this grain is the one that
    /// needs the same list, to decide whether a transfer's destination belongs to the player asking.
    /// A grain of its own for one query would be a second thing to keep in step with the first.
    /// </remarks>
    public Task<ImmutableArray<string>> GetLinkedWalletsAsync(CancellationToken ct);

    /// <summary>
    /// Moves every Relic the player still holds in the hotel out to <paramref name="wallet" />:
    /// mints each as an ERC-721 to that address, and marks it gone from the game.
    /// </summary>
    /// <remarks>
    /// The whole collection, because that is all the message asks for — it carries one address and
    /// no item list.
    /// </remarks>
    public Task<NftTransferResult> TransferAssetsAsync(string wallet, CancellationToken ct);
}

/// <summary>How a transfer out to a wallet ended, and how much of it happened.</summary>
[GenerateSerializer, Immutable]
public sealed record NftTransferResult
{
    [Id(0)]
    public required NftTransferOutcome Outcome { get; init; }

    /// <summary>How many Relics actually reached the wallet. Zero on every refusal.</summary>
    [Id(1)]
    public int Transferred { get; init; }
}

/// <summary>
/// Why a transfer was refused. The client prints the number into
/// <c>collectibles.transfer.error</c>, so these are few and mean one thing each.
/// </summary>
public enum NftTransferOutcome
{
    Transferred = 0,

    /// <summary>No chain is configured, so there is nowhere for a Relic to go.</summary>
    ChainUnavailable = 1,

    /// <summary>
    /// The destination is not an address this player has proved on the website. Refused rather than
    /// trusted: the address arrives from the client, and minting to whatever it sends would be a way
    /// to hand a stranger somebody else's collection.
    /// </summary>
    WalletNotLinked = 2,

    /// <summary>Nothing left in the hotel to move.</summary>
    NothingToTransfer = 3,

    /// <summary>The transfer fee could not be paid.</summary>
    CannotAffordFee = 4,

    /// <summary>The chain refused or did not answer; nothing moved and the fee was returned.</summary>
    Failed = 5,
}

/// <summary>How a stamp purchase ended, and what the balance is now.</summary>
[GenerateSerializer, Immutable]
public sealed record MintTokenPurchaseResult
{
    [Id(0)]
    public required bool Purchased { get; init; }

    [Id(1)]
    public required int Balance { get; init; }
}

/// <summary>
/// How a conversion ended. The client tells success from failure and nothing more, but the reason
/// decides what is logged and is the only way to tell a refusal from a bug afterwards.
/// </summary>
public enum MintOutcome
{
    Minted = 0,
    MintingDisabled = 1,

    /// <summary>The item is not in this player's inventory — or not there any more.</summary>
    NotOwned = 2,

    /// <summary>Its classname is not on the mintable list, or its window has closed.</summary>
    NotMintable = 3,
    NotEnoughStamps = 4,
    Failed = 5,

    /// <summary>Every copy of a limited edition has already been converted.</summary>
    EditionExhausted = 6,
}
