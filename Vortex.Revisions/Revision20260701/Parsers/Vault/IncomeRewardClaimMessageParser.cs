using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Vault.Enums;
using Vortex.Protocol.Messages.Incoming.Vault;

namespace Vortex.Revisions.Revision20260701.Parsers.Vault;

internal class IncomeRewardClaimMessageParser : IParser
{
    // Signed: Claim All is -1 on the wire (0xFF), and every real category fits in 0..127.
    public IMessageEvent Parse(IClientPacket packet) =>
        new IncomeRewardClaimMessage
        {
            Category = (VaultRewardCategoryType)unchecked((sbyte)packet.PopByte()),
        };
}
