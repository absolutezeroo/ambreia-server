using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Protocol.Messages.Outgoing.RewardVault;

[GenerateSerializer, Immutable]
public sealed record IncomeRewardClaimResponseMessageComposer : IComposer
{
    [Id(0)]
    public required RewardVaultCategoryType RewardCategory { get; init; }

    [Id(1)]
    public required bool Result { get; init; }
}
