using Vortex.Primitives.Networking;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Protocol.Messages.Incoming.RewardVault;

public record IncomeRewardClaimMessage : IMessageEvent
{
    public required RewardVaultCategoryType Category { get; init; }
}
