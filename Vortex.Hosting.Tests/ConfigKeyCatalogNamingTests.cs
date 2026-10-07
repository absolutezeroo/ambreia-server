using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Server;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// The RPG names its subscription the membership (M0 bis). An operator reads these keys in the
/// dashboard's config editor, so a Habbo name left behind there is the one place it still shows.
/// </summary>
public sealed class ConfigKeyCatalogNamingTests
{
    [Fact]
    public void NoKeyIsStillNamedAfterHabboClubOrPrizePools()
    {
        ConfigKeyCatalog
            .All.Select(d => d.Key)
            .Should()
            .NotContain(key =>
                key.StartsWith("club.")
                || key.StartsWith("prize_pools.")
                || key.Contains("habbo_club")
            );
    }

    [Theory]
    [InlineData("membership.gift_cycle_days")]
    [InlineData("membership.streak_grace_days")]
    [InlineData("membership.kickback_percent")]
    [InlineData("membership.badge_code")]
    [InlineData("membership.vip_badge_code")]
    [InlineData("earnings.membership.to_vault")]
    public void TheMembershipKeysAreDeclared(string key) =>
        ConfigKeyCatalog.Find(key).Should().NotBeNull();
}
