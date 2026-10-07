using Vortex.Primitives.Players;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Primitives.Events;

/// <summary>
/// Income accrued in the vault was cashed out. The wallet credits land in the ledger either way;
/// this names the category they came from, which is the part the ledger cannot say.
/// </summary>
public sealed record RewardVaultClaimedEvent(PlayerId PlayerId, string Category, int Rewards)
    : IEvent;

/// <summary>
/// Something new is waiting in a player's Earnings window. The vault is protocol-free, so the client
/// ping (IncomeRewardNotification) is sent by a handler of this event, not by the grain.
/// </summary>
public sealed record RewardVaultDepositedEvent(PlayerId PlayerId, RewardVaultCategoryType Category)
    : IEvent;
