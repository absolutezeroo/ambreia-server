using System;
using FluentAssertions;
using Vortex.Database.Entities.Players;
using Vortex.Inventory.Fulfillment;
using Vortex.Primitives.Players.Enums;
using Xunit;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// Bought Builders Club time and furni, and the three notifications the client tells apart.
/// </summary>
public sealed class BuildersClubGrantTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_first_purchase_creates_the_membership()
    {
        (PlayerSubscriptionEntity row, string? notification) = BuildersClubGrant.Apply(
            null,
            7,
            days: 14,
            furni: 0,
            Now
        );

        notification.Should().Be("builders_club.membership_made");
        row.SubscriptionType.Should().Be(SubscriptionType.BuildersClub);
        row.ExpiresAt.Should().Be(Now.AddDays(14));
        row.TotalDays.Should().Be(14);
        row.FirstSubscribedAt.Should().Be(Now);
    }

    [Fact]
    public void Buying_while_a_member_extends_from_the_current_expiry()
    {
        PlayerSubscriptionEntity current = Row(expiresAt: Now.AddDays(3), totalDays: 10);

        (PlayerSubscriptionEntity row, string? notification) = BuildersClubGrant.Apply(
            current,
            7,
            days: 31,
            furni: 0,
            Now
        );

        notification.Should().Be("builders_club.membership_extended");
        row.ExpiresAt.Should().Be(Now.AddDays(34));
        row.TotalDays.Should().Be(41);
    }

    [Fact]
    public void Buying_after_a_lapse_restarts_from_now()
    {
        PlayerSubscriptionEntity lapsed = Row(expiresAt: Now.AddDays(-20), totalDays: 10);

        (PlayerSubscriptionEntity row, string? notification) = BuildersClubGrant.Apply(
            lapsed,
            7,
            days: 7,
            furni: 0,
            Now
        );

        notification.Should().Be("builders_club.membership_renewed");
        row.ExpiresAt.Should().Be(Now.AddDays(7));
        row.TotalDays.Should().Be(17);
    }

    [Fact]
    public void An_add_on_raises_the_limit_and_says_nothing()
    {
        PlayerSubscriptionEntity current = Row(expiresAt: Now.AddDays(3), totalDays: 10);

        (PlayerSubscriptionEntity row, string? notification) = BuildersClubGrant.Apply(
            current,
            7,
            days: 0,
            furni: 250,
            Now
        );

        notification.Should().BeNull();
        row.BonusFurniLimit.Should().Be(250);
        row.ExpiresAt.Should().Be(Now.AddDays(3));
    }

    /// <summary>A staff-written row never counted its days; its span is credited first.</summary>
    [Fact]
    public void A_row_no_purchase_touched_is_credited_its_span_first()
    {
        PlayerSubscriptionEntity staffRow = Row(expiresAt: Now.AddDays(10), totalDays: 0);
        staffRow.FirstSubscribedAt = Now.AddDays(-20);

        (PlayerSubscriptionEntity row, _) = BuildersClubGrant.Apply(staffRow, 7, 5, 0, Now);

        row.TotalDays.Should().Be(35);
    }

    private static PlayerSubscriptionEntity Row(DateTime expiresAt, int totalDays) =>
        new()
        {
            PlayerEntityId = 7,
            SubscriptionType = SubscriptionType.BuildersClub,
            ExpiresAt = expiresAt,
            TotalDays = totalDays,
            FirstSubscribedAt = Now.AddDays(-30),
            PlayerEntity = null!,
        };
}
