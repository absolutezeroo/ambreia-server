using System.Collections.Immutable;
using System.IO;
using System.Linq;
using FluentAssertions;
using Vortex.Dashboard.API.Infrastructure;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// Reading the wearables out of the client's own figuredata.
/// </summary>
/// <remarks>
/// The fragments here are copied from the real file, tags and attribute order included. The one
/// that matters is the nesting: a set carries no type of its own, so a reader that does not follow
/// the enclosing settype produces three thousand garments that are all worn nowhere.
/// </remarks>
public sealed class FiguredataIndexTests
{
    private static ImmutableArray<FigureSet> Read(string xml) =>
        FiguredataIndex.Read(new StringReader(xml));

    private const string TwoTypes = """
        <figuredata>
          <sets>
            <settype type="sh" paletteid="3" mand_m_0="0" mand_f_0="0">
              <set id="290" gender="M" club="0" colorable="1" selectable="1" preselectable="1" />
              <set id="3016" gender="U" club="2" colorable="1" selectable="1" preselectable="0" />
            </settype>
            <settype type="ch" paletteid="3">
              <set id="290" gender="F" club="0" colorable="1" selectable="1" />
            </settype>
          </sets>
        </figuredata>
        """;

    [Fact]
    public void A_set_takes_its_type_from_the_settype_it_sits_in()
    {
        ImmutableArray<FigureSet> sets = Read(TwoTypes);

        // Id 290 exists under both types and means a different garment in each, which is why the
        // filter an operator writes has to carry the type as well as the number.
        sets.Should()
            .HaveCount(3)
            .And.ContainSingle(s => s.Id == 290 && s.Type == "sh")
            .And.ContainSingle(s => s.Id == 290 && s.Type == "ch");
    }

    [Fact]
    public void Club_is_anything_but_zero()
    {
        ImmutableArray<FigureSet> sets = Read(TwoTypes);

        sets.Single(s => s.Id == 3016).Club.Should().BeTrue();
        sets.Single(s => s.Id == 290 && s.Type == "sh").Club.Should().BeFalse();
    }

    [Fact]
    public void A_set_with_no_gender_is_unisex()
    {
        Read(
                """<figuredata><sets><settype type="sh"><set id="1" /></settype></sets></figuredata>"""
            )
            .Single()
            .Gender.Should()
            .Be("U");
    }

    [Theory]
    // Truncated mid-document: the file was being written while this read it.
    [InlineData("<figuredata><sets><settype type=\"sh\"><set id=\"1\"")]
    // Not figuredata at all: the asset root points somewhere else entirely.
    [InlineData("{\"not\":\"xml\"}")]
    [InlineData("")]
    public void A_file_that_cannot_be_read_is_no_garments_rather_than_a_throw(string xml) =>
        Read(xml).Should().BeEmpty();

    [Fact]
    public void A_set_outside_any_settype_is_skipped()
    {
        // It names no part of the body, so nothing could be said about where it is worn -- and a
        // garment filed under the previous settype would be a picker row that lies.
        Read(
                """
                <figuredata><sets>
                  <settype type="sh"><set id="290" /></settype>
                  <set id="999" />
                </sets></figuredata>
                """
            )
            .Should()
            .ContainSingle(s => s.Id == 290);
    }
}
