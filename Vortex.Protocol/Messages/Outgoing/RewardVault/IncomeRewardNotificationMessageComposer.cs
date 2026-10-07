using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Protocol.Messages.Outgoing.RewardVault;

/// <summary>
/// Tells the client one of its income reward categories has moved, so that it comes back and asks
/// for the status again.
/// </summary>
/// <remarks>
/// <para>
/// It carries nothing but the category because it is not a notification in its own right: the
/// client's handler for it does one thing, <c>_sessionDataManager.getIncomeRewardStatus()</c>
/// (EarningsController.as:139), and that follow-up is already answered by
/// <c>IncomeRewardStatusMessageHandler</c> off <c>PlayerRewardVaultGrain</c>.
/// </para>
/// <para>
/// Sent by <c>RewardVaultDepositedNotifier</c> whenever <c>PlayerRewardVaultGrain.EarnAsync</c> keeps a
/// new row. One byte, verified against the official parser and against a 2026-09-18 capture whose
/// value, 5, is this enum's <see cref="RewardVaultCategoryType.LevelProgression"/> arriving beside
/// a reward-track advance.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record IncomeRewardNotificationMessageComposer : IComposer
{
    [Id(0)]
    public required RewardVaultCategoryType RewardCategory { get; init; }
}
