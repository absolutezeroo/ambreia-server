using Vortex.Primitives.Players;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Primitives.Events;

/// <summary>
/// A Relic was minted: a piece of furniture was destroyed and a unique, numbered asset took its
/// place. The provenance ledger already records the asset's own history; this is the line that puts
/// the act on the player's timeline next to the furniture that paid for it.
/// </summary>
public sealed record RelicMintedEvent(
    PlayerId PlayerId,
    int AssetId,
    int DefinitionId,
    int SerialNumber,
    int StampCost
) : IEvent;

/// <summary>Mint stamps were bought. The stamps are the gate on how many Relics can exist.</summary>
public sealed record MintTokensPurchasedEvent(PlayerId PlayerId, int Quantity, int Cost) : IEvent;

/// <summary>Something was bought from the collectibles store.</summary>
public sealed record NftStorePurchasedEvent(PlayerId PlayerId, string ProductCode, int Price)
    : IEvent;

/// <summary>Pending collectible claims were collected.</summary>
public sealed record NftClaimsCollectedEvent(PlayerId PlayerId, int Count) : IEvent;

/// <summary>
/// Income accrued in the vault was cashed out. The wallet credits land in the ledger either way;
/// this names the category they came from, which is the part the ledger cannot say.
/// </summary>
public sealed record VaultIncomeClaimedEvent(PlayerId PlayerId, string Category, int Rewards)
    : IEvent;

/// <summary>
/// Something new is waiting in a player's Earnings window. The vault is protocol-free, so the client
/// ping (IncomeRewardNotification) is sent by a handler of this event, not by the grain.
/// </summary>
public sealed record VaultIncomeDepositedEvent(PlayerId PlayerId, VaultRewardCategoryType Category)
    : IEvent;

/// <summary>
/// A whole-avatar Relic was worn or taken off. Worth recording because it changes what everyone else
/// sees the account as, which is the same reason a figure change is recorded.
/// </summary>
public sealed record NftAvatarWornEvent(PlayerId PlayerId, int? CopyId) : IEvent;
