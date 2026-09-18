using FluentAssertions;
using Vortex.Primitives.Furniture;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// The collectibles coin, whose value is in the digits that END its classname — a different
/// convention from <c>CF_</c>, read off <c>FurnitureNftCreditLogic.initialize</c> in the client.
///
/// <para>
/// Pinned for the same reason the credit one is, plus one of its own: the client prints "this item
/// contains N emeralds" from its own reading of the name before the player confirms, so a server
/// that read it differently would promise one figure and pay another.
/// </para>
/// </summary>
public sealed class EmeraldFurniValueTests
{
    [Theory]
    // The five this hotel's furnidata actually ships.
    [InlineData("nft_emerald_10", 10)]
    [InlineData("nft_emerald_50", 50)]
    [InlineData("nft_emerald_heart_2000", 2000)]
    [InlineData("nft_emerald_xmastree_2000", 2000)]
    [InlineData("nft_emerald_eggmerald", 2000)]
    // Hard-coded in the client and absent here; kept so the two tables cannot drift apart unnoticed.
    [InlineData("nft_emerald_emerhand", 15000)]
    public void TryParse_ReadsTheTrailingDigitsOrTheNamedValue(string name, int expected)
    {
        EmeraldFurniValue.TryParse(name, out int emeralds).Should().BeTrue();
        emeralds.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("chair_norja")]
    // A credit furni is the other convention and must not fall through to this one: "CF_50_coin"
    // ends in no digit, but "CF_1000_goldenkey" ending in one would pay emeralds for credits.
    [InlineData("CF_10_coin_gold")]
    [InlineData("nft_emerald_")]
    [InlineData("nft_emerald_0")]
    public void TryParse_RefusesAnythingThatIsNotAnEmeraldFurniWithAValue(string name)
    {
        // All-or-nothing, like the credit coin: the room consumes the furniture either way, so a
        // name it cannot price has to pay nothing rather than pay zero.
        EmeraldFurniValue.TryParse(name, out int emeralds).Should().BeFalse();
        emeralds.Should().Be(0);
    }

    [Fact]
    public void TryParse_DoesNotReadASignAsPartOfTheValue()
    {
        // The client's own regex is \d+$, which stops at the minus too — a name ending "-50" is
        // worth 50 to it, and would be worth -50 to a lenient parse.
        EmeraldFurniValue.TryParse("nft_emerald_-50", out int emeralds).Should().BeTrue();
        emeralds.Should().Be(50);
    }
}
