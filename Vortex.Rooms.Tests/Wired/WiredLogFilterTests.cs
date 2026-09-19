using FluentAssertions;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Rooms.Wired.Logs;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// Both log enums are byte-backed and the client sends their ordinals as ints. The conversion
/// between the two threw on every request, so the log dialog was dead for every filter, including
/// the "all" one nobody thinks of as a filter.
/// </summary>
public sealed class WiredLogFilterTests
{
    [Theory]
    [InlineData(0, WiredLogLevel.Info)]
    [InlineData(1, WiredLogLevel.Warning)]
    [InlineData(2, WiredLogLevel.Error)]
    public void ADeclaredOrdinal_ResolvesToItsLevel(int ordinal, WiredLogLevel expected)
    {
        WiredLogFilter.FromWire<WiredLogLevel>(ordinal).Should().Be(expected);
    }

    [Fact]
    public void ADeclaredOrdinal_ResolvesToItsSource()
    {
        WiredLogFilter.FromWire<WiredLogSource>(3).Should().Be(WiredLogSource.Action);
    }

    [Theory]
    [InlineData(-1)] // the client's "no filter" sentinel
    [InlineData(3)]
    [InlineData(256)] // would wrap onto Info under a blind cast to the byte-backed enum
    [InlineData(int.MaxValue)]
    public void AnOrdinalNoLevelDeclares_MeansNoFilter(int ordinal)
    {
        WiredLogFilter.FromWire<WiredLogLevel>(ordinal).Should().BeNull();
    }
}
