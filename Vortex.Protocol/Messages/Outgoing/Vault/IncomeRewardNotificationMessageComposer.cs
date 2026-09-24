using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Protocol.Messages.Outgoing.Vault;

/// <summary>
/// Tells the client one of its income reward categories has moved, so that it comes back and asks
/// for the status again.
/// </summary>
/// <remarks>
/// <para>
/// It carries nothing but the category because it is not a notification in its own right: the
/// client's handler for it does one thing, <c>_sessionDataManager.getIncomeRewardStatus()</c>
/// (EarningsController.as:139), and that follow-up is already answered by
/// <c>IncomeRewardStatusMessageHandler</c> off <c>PlayerVaultGrain</c>.
/// </para>
/// <para>
/// Sent by <c>VaultIncomeDepositedNotifier</c> whenever <c>PlayerVaultGrain.EarnAsync</c> keeps a
/// new row. One byte, verified against the official parser and against a 2026-09-18 capture whose
/// value, 5, is this enum's <see cref="VaultRewardCategoryType.LevelProgression"/> arriving beside
/// a reward-track advance.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record IncomeRewardNotificationMessageComposer : IComposer
{
    [Id(0)]
    public required VaultRewardCategoryType RewardCategory { get; init; }
}
