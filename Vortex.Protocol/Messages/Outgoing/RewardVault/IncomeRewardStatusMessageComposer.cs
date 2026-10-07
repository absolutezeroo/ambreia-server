using System.Collections.Generic;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.RewardVault;

namespace Vortex.Protocol.Messages.Outgoing.RewardVault;

[GenerateSerializer, Immutable]
public sealed record IncomeRewardStatusMessageComposer : IComposer
{
    [Id(0)]
    public required List<IncomeRewardSnapshot> IncomeRewards { get; init; }
}
