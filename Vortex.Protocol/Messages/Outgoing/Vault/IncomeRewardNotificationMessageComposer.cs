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
/// <b>Nothing sends this yet, and that is not an oversight of the wire.</b>
/// <c>PlayerVaultGrain.AddIncomeRewardAsync</c> is the only thing that could ever put a reward in
/// front of a player, and no caller in this solution invokes it — the vault can be read and claimed
/// from, never filled. The ping belongs at the end of that method, and the day something grants a
/// reward is the day it is worth adding; sending it before then would ask the client to re-read a
/// list that never changes. The shape is settled, so that is the only piece missing: one byte,
/// verified against the official parser and against a 2026-09-18 capture whose value, 5, is this
/// enum's <see cref="VaultRewardCategoryType.LevelProgression"/> arriving beside a reward-track
/// advance.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record IncomeRewardNotificationMessageComposer : IComposer
{
    [Id(0)]
    public required VaultRewardCategoryType RewardCategory { get; init; }
}
