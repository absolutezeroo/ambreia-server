using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.RewardVault.Enums;
using Vortex.Protocol.Messages.Outgoing.RewardVault;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Notifications;

/// <summary>
/// One byte, not an int. The official parser reads the category with readByte, and the capture that
/// prompted this carried a single 0x05 — LevelProgression — in a five-byte frame.
/// </summary>
public sealed class IncomeRewardNotificationWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Theory]
    [InlineData(RewardVaultCategoryType.Tutorial, "00")]
    [InlineData(RewardVaultCategoryType.LevelProgression, "05")]
    [InlineData(RewardVaultCategoryType.SnowStorm, "0a")]
    public void Serializer_WritesTheCategoryAsASingleByte(
        RewardVaultCategoryType category,
        string expectedBody
    )
    {
        IncomeRewardNotificationMessageComposer composer = new() { RewardCategory = category };

        byte[] frame = Revision
            .Serializers[typeof(IncomeRewardNotificationMessageComposer)]
            .Serialize(composer)
            .ToArray();

        // Four bytes of length and two of header in front, then the body.
        frame.Should().HaveCount(7);
        Convert.ToHexString(frame.Skip(6).ToArray()).ToLowerInvariant().Should().Be(expectedBody);
    }
}
