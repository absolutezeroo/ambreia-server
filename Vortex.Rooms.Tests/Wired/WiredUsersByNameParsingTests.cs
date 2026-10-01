using FluentAssertions;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Selectors;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// The name list of "users by name". The client joins the text area's lines with tabs
/// (<c>UsersByName.readStringParamFromForm</c>); the box split on '/', so a list of two names
/// selected nobody.
/// </summary>
public sealed class WiredUsersByNameParsingTests
{
    [Fact]
    public void TheClientsTabJoinedLines_AreOneNameEach()
    {
        WiredSelectorEntitiesByName
            .ParseNames("Alice\tbob\t\tCarol ")
            .Should()
            .BeEquivalentTo("Alice", "bob", "Carol");
    }

    [Fact]
    public void NamesMatchWhateverTheirCase()
    {
        WiredSelectorEntitiesByName.ParseNames("alice").Should().Contain("ALICE");
    }
}
