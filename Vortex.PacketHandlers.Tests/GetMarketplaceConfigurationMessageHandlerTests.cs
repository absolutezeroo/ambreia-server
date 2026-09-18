using FluentAssertions;
using Vortex.PacketHandlers.Marketplace;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>
/// The client reads this field as hours and divides it by 24 to name a number of days
/// (`MarketplaceView.as:152`), while the row stores seconds because that is what the purchase
/// grain expires an offer on. The whole point of the conversion is that those two units never meet
/// on the wire again.
/// </summary>
public sealed class GetMarketplaceConfigurationMessageHandlerTests
{
    [Theory]
    // The shipped default: three days.
    [InlineData(259200, 72)]
    [InlineData(3600, 1)]
    [InlineData(86400, 24)]
    // A week, which is what the official server advertises.
    [InlineData(604800, 168)]
    // Rounded up: anything inside an hour still lasts an hour as far as the dialog is concerned.
    [InlineData(1, 1)]
    [InlineData(3599, 1)]
    [InlineData(3601, 2)]
    // A row misconfigured to nothing says nothing rather than going negative.
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void ToExpirationHours_ConvertsSecondsToWholeHours(int seconds, int expected)
    {
        GetMarketplaceConfigurationMessageHandler.ToExpirationHours(seconds).Should().Be(expected);
    }

    [Fact]
    public void ToExpirationHours_NeverReturnsTheSecondsItWasGiven()
    {
        // The bug this replaces sent the seconds straight through, which the client rendered as
        // 10800 days. Anything above an hour has to shrink.
        GetMarketplaceConfigurationMessageHandler
            .ToExpirationHours(259200)
            .Should()
            .BeLessThan(259200);
    }
}
