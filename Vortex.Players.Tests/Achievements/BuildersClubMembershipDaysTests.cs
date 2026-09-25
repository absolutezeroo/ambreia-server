using System;
using FluentAssertions;
using Xunit;

namespace Vortex.Players.Tests.Achievements;

/// <summary>How long a player has been a Builders Club member — the BuildersClub achievement's bar.</summary>
public sealed class BuildersClubMembershipDaysTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    // Bought 14 days an hour ago: the started day counts.
    [InlineData(14, 13.96, 1)]
    // 10 of 31 days left.
    [InlineData(31, 10, 21)]
    // Ten and a half days lived: eleven started.
    [InlineData(31, 20.5, 11)]
    // Expired: every granted day was lived, the gap after it is not counted.
    [InlineData(45, -30, 45)]
    public void Counts_the_started_days_of_what_was_granted(
        int totalDays,
        double expiresInDays,
        int expected
    ) =>
        BuildersClubService
            .MembershipDays(Now.AddDays(-100), Now.AddDays(expiresInDays), totalDays, Now)
            .Should()
            .Be(expected);

    [Fact]
    public void A_row_without_a_total_is_one_span_from_its_start() =>
        BuildersClubService
            .MembershipDays(Now.AddDays(-20), Now.AddDays(10), totalDays: 0, Now)
            .Should()
            .Be(20);
}
