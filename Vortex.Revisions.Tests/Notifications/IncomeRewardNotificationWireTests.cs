using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Vault.Enums;
using Vortex.Protocol.Messages.Outgoing.Vault;
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
    [InlineData(VaultRewardCategoryType.Tutorial, "00")]
    [InlineData(VaultRewardCategoryType.LevelProgression, "05")]
    [InlineData(VaultRewardCategoryType.SnowStorm, "0a")]
    public void Serializer_WritesTheCategoryAsASingleByte(
        VaultRewardCategoryType category,
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
