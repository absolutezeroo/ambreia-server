using FluentAssertions;
using Vortex.Rooms.Camera.Render;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// Tracing an avatar's part back to the bundle that holds it.
/// </summary>
/// <remarks>
/// An avatar reaches the camera already taken apart — one sprite per body part — and a part names
/// itself rather than its library. The xml below is the shape of the hotel's real figuremap, which
/// carries about four thousand of these.
/// </remarks>
public sealed class FiguremapIndexTests
{
    private const string Figuremap = """
        <?xml version="1.0"?>
        <map>
          <lib id="hh_human_body" revision="38955">
            <part id="1" type="bd"/>
            <part id="1" type="sh"/>
          </lib>
          <lib id="shirt_F_trippyshirt_hearts_nft" revision="67121">
            <part id="4316" type="ch"/>
            <part id="4316" type="ls"/>
          </lib>
        </map>
        """;

    [Theory]
    [InlineData("h_std_bd_1_2_0", "hh_human_body")]
    [InlineData("h_std_ch_4316_3_0", "shirt_F_trippyshirt_hearts_nft")]
    public void APartNamesItsLibrary(string assetName, string expected)
    {
        FiguremapIndex.Read(Figuremap).TryLibrary(assetName, out string? library).Should().BeTrue();

        FiguremapIndex.Read(Figuremap).TryLibrary(assetName, out library);

        library.Should().Be(expected);
    }

    /// <summary>
    /// An id belongs to a library per <i>type</i>, not on its own: id 1 is a body under
    /// <c>bd</c> and nothing under <c>ch</c>. Keying on the id alone would dress every avatar in
    /// whichever library happened to be read last.
    /// </summary>
    [Fact]
    public void AnIdWithoutItsTypeIsNotEnough()
    {
        FiguremapIndex.Read(Figuremap).TryLibrary("h_std_ch_1_2_0", out _).Should().BeFalse();
    }

    /// <summary>
    /// The action segment carries underscores of its own, so type and id are counted from the right
    /// past direction and frame — never from the left.
    /// </summary>
    [Fact]
    public void AnActionWithUnderscoresStillResolves()
    {
        FiguremapIndex
            .Read(Figuremap)
            .TryLibrary("h_std_sit_bd_1_2_0", out string? library)
            .Should()
            .BeTrue();

        library.Should().Be("hh_human_body");
    }

    /// <summary>
    /// The figuremap is incomplete and the client copes, so this has to too.
    /// </summary>
    /// <remarks>
    /// These three names come from a real photo's unresolved list. The map declares
    /// <c>cc:2065</c> and not <c>lc:2065</c> or <c>rc:2065</c>, and <c>hr:501</c> and not
    /// <c>hrb:501</c> — the sleeves and the back of the hair live in those libraries anyway.
    /// </remarks>
    [Theory]
    [InlineData("h_std_lc_2065_2_0", "jacket_M_general")]
    [InlineData("h_std_rc_2065_2_0", "jacket_M_general")]
    [InlineData("h_std_hrb_501_2_0", "hh_human_hair")]
    public void ASubPartFallsBackToTheLibraryItIsPackedWith(string assetName, string expected)
    {
        FiguremapIndex index = FiguremapIndex.Read(
            """
            <?xml version="1.0"?>
            <map>
              <lib id="jacket_M_general"><part id="2065" type="cc"/></lib>
              <lib id="hh_human_hair"><part id="501" type="hr"/></lib>
              <lib id="hh_human_shirt"><part id="501" type="ch"/></lib>
            </map>
            """
        );

        index.TryLibrary(assetName, out string? library).Should().BeTrue();

        library.Should().Be(expected);
    }

    /// <summary>
    /// The fallback carries the type with it. Id 501 is the hair library under <c>hr</c> and the
    /// shirt library under <c>ch</c>, so falling back on the id alone would dress an avatar in
    /// whichever of the two was read last.
    /// </summary>
    [Fact]
    public void TheFallbackIsPerTypeAndNotPerId()
    {
        FiguremapIndex index = FiguremapIndex.Read(
            """
            <?xml version="1.0"?>
            <map>
              <lib id="hh_human_hair"><part id="501" type="hr"/></lib>
              <lib id="hh_human_shirt"><part id="501" type="ch"/></lib>
            </map>
            """
        );

        index.TryLibrary("h_std_ls_501_2_0", out string? sleeve).Should().BeTrue();
        sleeve.Should().Be("hh_human_shirt", "a sleeve is packed with the shirt");

        index.TryLibrary("h_std_hrb_501_2_0", out string? hair).Should().BeTrue();
        hair.Should().Be("hh_human_hair", "the back of the hair is packed with the hair");
    }

    [Theory]
    [InlineData("chair_basic_64_a_0_0")]
    [InlineData("short")]
    [InlineData("")]
    public void SomethingThatIsNotAPart_DoesNotResolve(string assetName)
    {
        FiguremapIndex.Read(Figuremap).TryLibrary(assetName, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<map><lib><part/></lib>")]
    public void AFiguremapThatIsNotOne_IsAnEmptyIndex(string xml)
    {
        FiguremapIndex.Read(xml).Count.Should().Be(0);
    }
}
