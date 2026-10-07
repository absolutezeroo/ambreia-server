using Orleans;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Primitives.Orleans.Snapshots.RewardVault;

[GenerateSerializer, Immutable]
public sealed record IncomeRewardSnapshot
{
    [Id(0)]
    public required RewardVaultCategoryType RewardCategory { get; init; }

    [Id(1)]
    public required RewardVaultType RewardType { get; init; }

    [Id(2)]
    public required int Amount { get; init; }

    [Id(3)]
    public required string ProductCode { get; init; }
}
