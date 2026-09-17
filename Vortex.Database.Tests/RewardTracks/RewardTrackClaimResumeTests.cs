using System;
using FluentAssertions;
using Vortex.Database.Entities.RewardTracks;
using Xunit;

namespace Vortex.Database.Tests.RewardTracks;

/// <summary>
/// Which claims a later click is allowed to finish.
/// </summary>
/// <remarks>
/// The claim row commits before the bundle is granted, so the entitlement is spent from before the
/// rewards exist. A stop in that window used to be a dead end — the operation id was minted per
/// attempt and written nowhere, so the prize was gone and every later click was answered
/// AlreadyClaimed. The id lives on the row now, and this predicate is what decides whether the
/// click is a retry or a duplicate. Getting it wrong in either direction is a bug with a player
/// attached, so it is a property rather than a condition buried in a branch.
/// </remarks>
public sealed class RewardTrackClaimResumeTests
{
    private static PlayerRewardTrackClaimEntity Claim(Guid operation, string summary) =>
        new()
        {
            PlayerEntityId = 7,
            TrackId = "track",
            PrizeId = "prize",
            ClaimedAt = DateTime.UtcNow,
            OperationId = operation,
            GrantedSummary = summary,
        };

    [Fact]
    public void AClaimRecordedButNeverDelivered_IsResumable()
    {
        Claim(Guid.NewGuid(), string.Empty)
            .IsDeliveryUnfinished.Should()
            .BeTrue("the entitlement is spent and nothing was handed over");
    }

    [Fact]
    public void AClaimThatDelivered_IsNot()
    {
        Claim(Guid.NewGuid(), "currency:0x100")
            .IsDeliveryUnfinished.Should()
            .BeFalse("resuming it would ask the granters to hand the bundle over a second time");
    }

    [Fact]
    public void AClaimFromBeforeTheColumnExisted_IsLeftAlone()
    {
        Claim(Guid.Empty, string.Empty)
            .IsDeliveryUnfinished.Should()
            .BeFalse("there is no operation to resume it under, and inventing one is guessing");
    }
}
