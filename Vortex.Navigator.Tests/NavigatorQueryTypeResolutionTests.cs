using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Database.Context;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Navigator.Tests;

/// <summary>
/// Every navigator tab sends a search code the client hardcodes. Nothing seeded
/// <c>navigator_top_level_contexts</c>, so on a fresh hotel the lookup finds no row for any of them
/// and the built-in table has to answer. The navigator serves five queries; every other code
/// resolves to <c>null</c> -- "not served" -- and answers nothing, rather than the whole room table.
/// </summary>
public sealed class NavigatorQueryTypeResolutionTests
{
    // ResolveQueryType reads cached reference data, never the database, so the factory is never
    // touched -- but the constructor still wants one.
    private static readonly NavigatorProvider Provider = new(
        FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ => null),
        NullLogger<NavigatorProvider>.Instance
    );

    [Theory]
    [InlineData(NavigatorSearchCodes.MyRooms, NavigatorQueryType.MyRooms)]
    [InlineData(NavigatorSearchCodes.FriendsRooms, NavigatorQueryType.FriendsRooms)]
    [InlineData(NavigatorSearchCodes.MyGuildBases, NavigatorQueryType.MyGroups)]
    [InlineData(NavigatorSearchCodes.GuildBases, NavigatorQueryType.GuildBases)]
    [InlineData(NavigatorSearchCodes.TextSearch, NavigatorQueryType.TextSearch)]
    [InlineData(NavigatorSearchCodes.MyWorldView, NavigatorQueryType.MyRooms)]
    [InlineData(NavigatorSearchCodes.HotelView, NavigatorQueryType.TextSearch)]
    public void KnownSearchCode_ResolvesWithoutAnyDatabaseConfiguration(
        string searchCode,
        NavigatorQueryType expected
    ) => Provider.ResolveQueryType(searchCode).Should().Be(expected);

    /// <summary>Everything the old navigator served beyond the five queries, plus codes nobody
    /// ever taught the server about: not served, so not "every room".</summary>
    [Theory]
    [InlineData("popular")]
    [InlineData("official")]
    [InlineData("official_view")]
    [InlineData("roomads_view")]
    [InlineData("history")]
    [InlineData("history_freq")]
    [InlineData("highest_score")]
    [InlineData("recommended")]
    [InlineData("favorites")]
    [InlineData("with_rights")]
    [InlineData("with_friends")]
    [InlineData("staffpicks")]
    [InlineData("new_ads")]
    [InlineData("top_promotions")]
    [InlineData("competition")]
    [InlineData("categories")]
    [InlineData("category__3")]
    [InlineData("eventcategory__1")]
    [InlineData("something_nobody_implemented")]
    public void RemovedOrUnknownSearchCode_ResolvesToNothing(string searchCode) =>
        Provider.ResolveQueryType(searchCode).Should().BeNull();

    /// <summary>The stored numbers outlive the removed queries: a row still carrying 10 (popular)
    /// must not be mistaken for a defined value.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(17)]
    [InlineData(18)]
    public void ARemovedQueryTypeNumber_IsNotADefinedValue(int stored) =>
        System.Enum.IsDefined((NavigatorQueryType)stored).Should().BeFalse();

    /// <summary>The surviving numbers keep the values they were persisted with.</summary>
    [Fact]
    public void TheSurvivingQueryTypes_KeepTheirStoredNumbers()
    {
        ((int)NavigatorQueryType.None).Should().Be(0);
        ((int)NavigatorQueryType.MyRooms).Should().Be(1);
        ((int)NavigatorQueryType.FriendsRooms).Should().Be(3);
        ((int)NavigatorQueryType.TextSearch).Should().Be(14);
        ((int)NavigatorQueryType.MyGroups).Should().Be(15);
        ((int)NavigatorQueryType.GuildBases).Should().Be(16);
    }
}
