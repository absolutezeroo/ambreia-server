using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.RewardVault;

namespace Vortex.Revisions.Revision20260701.Parsers.RewardVault;

internal class IncomeRewardStatusMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) => new IncomeRewardStatusMessage();
}
