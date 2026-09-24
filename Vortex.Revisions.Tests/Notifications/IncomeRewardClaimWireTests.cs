using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Vault.Enums;
using Vortex.Protocol.Messages.Incoming.Vault;
using Vortex.Protocol.Messages.Outgoing.Vault;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Notifications;

/// <summary>
/// The client's Claim All sends category -1 as one byte (EarningsView.windowProcedure,
/// _SafeCls_2320: new Byte(param1)) and reads the response category back with a signed readByte.
/// Read unsigned, it arrived as 255, matched no row, and the button re-enabled with nothing claimed.
/// </summary>
public sealed class IncomeRewardClaimWireTests
{
    // Vortex.Revisions.Revision20260701.MessageEvent.IncomeRewardClaimMessageEvent (809); that
    // class is internal to Vortex.Revisions and not visible here, so the header is copied like
    // PostureAndSignWireLayoutTests does for ChangePostureMessageEvent/SignMessageEvent.
    private const int IncomeRewardClaimMessageEvent = 809;

    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket ClientPacketWithBody(byte body)
    {
        ServerPacket sp = new(IncomeRewardClaimMessageEvent);
        sp.WriteByte(body);
        return new ClientPacket(IncomeRewardClaimMessageEvent, sp.ToArray());
    }

    [Theory]
    [InlineData(0xFF, VaultRewardCategoryType.All)]
    [InlineData(0x02, VaultRewardCategoryType.Achievements)]
    [InlineData(0x0D, VaultRewardCategoryType.Agency)]
    public void Parser_ReadsTheCategorySigned(byte wire, VaultRewardCategoryType expected)
    {
        IncomeRewardClaimMessage message = (IncomeRewardClaimMessage)
            Revision.Parsers[IncomeRewardClaimMessageEvent].Parse(ClientPacketWithBody(wire));

        message.Category.Should().Be(expected);
    }

    [Theory]
    [InlineData(VaultRewardCategoryType.All, "ff01")]
    [InlineData(VaultRewardCategoryType.HabboClub, "0400")]
    public void Response_WritesTheCategoryAsOneSignedByte(
        VaultRewardCategoryType category,
        string expectedBody
    )
    {
        bool result = category == VaultRewardCategoryType.All;

        byte[] frame = Revision
            .Serializers[typeof(IncomeRewardClaimResponseMessageComposer)]
            .Serialize(
                new IncomeRewardClaimResponseMessageComposer
                {
                    RewardCategory = category,
                    Result = result,
                }
            )
            .ToArray();

        Convert.ToHexString(frame.Skip(6).ToArray()).ToLowerInvariant().Should().Be(expectedBody);
    }
}
