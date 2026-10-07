using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Vortex.Furniture;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.StuffData;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Stuff data is written into the extra-data blob by one component and read back by another, and the
/// two disagreed: the writer applies a camelCase naming policy (<c>{"data":"5"}</c>) while the
/// factory deserialized case-sensitively into <c>Data</c>, so every saved value came back as the
/// type's default. Nothing failed loudly — a dice face, a gate's open flag or a trophy's inscription
/// simply reset on the next load — which is exactly the kind of regression that needs a test rather
/// than a comment.
///
/// <para>
/// The four tests below are that regression, held. Everything after them is the rest of the contract:
/// eight encodings, each written the way <c>PersistStuffDataAsync</c> writes it and read the way a
/// room activation reads it. The two halves go through different writers on purpose — the original
/// tests drive <c>ExtraData.UpdateSection</c>, which applies camelCase to a plain object, and the
/// later ones drive the node the persist path builds, which keeps Pascal. Both spellings are in the
/// furniture table, and only the reader being case-insensitive makes that survivable.
/// </para>
/// </summary>
public sealed class StuffDataRoundTripTests
{
    private static readonly StuffDataFactory Factory = new();

    private static string WriteStuffSection(object section)
    {
        ExtraData extraData = new(null);

        extraData.UpdateSection(ExtraDataSectionType.STUFF, section);

        return extraData.GetJsonString();
    }

    [Fact]
    public void AStuffSectionOfTheWrongShape_ReadsAsTheDefault()
    {
        // Valid JSON, wrong type for StringStuffData.Data: an import or a hand edit can write this,
        // and it is read inside a room's activation.
        IStuffData read = Factory.CreateStuffDataFromJson(
            StuffDataType.StringKey,
            """{"stuff":{"data":5}}"""
        );

        read.Should().BeEquivalentTo(Factory.CreateStuffData(StuffDataType.StringKey));
    }

    [Fact]
    public void LegacyStuffData_SurvivesAWriteThenRead()
    {
        string blob = WriteStuffSection(new { Data = "5" });

        IStuffData read = Factory.CreateStuffDataFromJson(StuffDataType.LegacyKey, blob);

        read.GetLegacyString().Should().Be("5");
    }

    [Fact]
    public void LegacyStuffData_SurvivesTheExtraDataOverload()
    {
        string blob = WriteStuffSection(new { Data = "owner\t29-07-2026\thello" });

        IStuffData read = Factory.CreateStuffDataFromExtraData(
            StuffDataType.LegacyKey,
            new ExtraData(blob)
        );

        read.GetLegacyString().Should().Be("owner\t29-07-2026\thello");
    }

    [Fact]
    public void UniqueMarkers_SurviveToo()
    {
        string blob = WriteStuffSection(
            new
            {
                Data = "1",
                UniqueNumber = 3,
                UniqueSeries = 100,
            }
        );

        IStuffData read = Factory.CreateStuffDataFromJson(StuffDataType.LegacyKey, blob);

        read.UniqueNumber.Should().Be(3);
        read.UniqueSeries.Should().Be(100);
        read.IsUnique().Should().BeTrue();
    }

    [Fact]
    public void AbsentSection_FallsBackToADefaultInstance()
    {
        IStuffData read = Factory.CreateStuffDataFromJson(StuffDataType.LegacyKey, "{}");

        read.GetLegacyString().Should().Be("0");
    }

    /// <summary>Every encoding builds, and reports the type it was asked for.</summary>
    [Theory]
    [InlineData(StuffDataType.LegacyKey)]
    [InlineData(StuffDataType.MapKey)]
    [InlineData(StuffDataType.StringKey)]
    [InlineData(StuffDataType.VoteKey)]
    [InlineData(StuffDataType.EmptyKey)]
    [InlineData(StuffDataType.NumberKey)]
    [InlineData(StuffDataType.HighscoreKey)]
    [InlineData(StuffDataType.CrackableKey)]
    public void EveryEncodingSurvivesAnEmptyRoundTrip(StuffDataType type)
    {
        IStuffData written = Factory.CreateStuffData(type);
        IStuffData read = Factory.CreateStuffDataFromJson(type, Persist(written));

        read.StuffType.Should().Be(written.StuffType);
        read.GetBitmask().Should().Be(written.GetBitmask());
    }

    /// <summary>
    /// The legacy encoding is the one most furniture uses: a die's face, a gate's open flag, a
    /// dimmer's preset are all a string that the client reads as a state.
    /// </summary>
    [Fact]
    public void ALegacyStateSurvives()
    {
        IStuffData written = Factory.CreateStuffData(StuffDataType.LegacyKey);
        written.SetState("4");

        IStuffData read = Factory.CreateStuffDataFromJson(
            StuffDataType.LegacyKey,
            Persist(written)
        );

        read.GetLegacyString().Should().Be("4");
        read.GetState().Should().Be(4);
    }

    [Fact]
    public void AMapsKeysAndValuesSurvive()
    {
        IStuffData written = Factory.CreateStuffData(StuffDataType.MapKey);
        ((IMapStuffData)written).Data["figure"] = "hd-180-1.ch-255-66";
        ((IMapStuffData)written).Data["name"] = "Mannequin";

        IMapStuffData read = (IMapStuffData)
            Factory.CreateStuffDataFromJson(StuffDataType.MapKey, Persist(written));

        read.Data.Should().Contain("figure", "hd-180-1.ch-255-66").And.Contain("name", "Mannequin");
    }

    /// <summary>
    /// A fresh string list is seeded with one default entry by its constructor, and the round trip
    /// preserves the list exactly — seed included. Asserting on the whole list rather than on
    /// "contains" is deliberate: the client indexes into it, so an extra or reordered element is a
    /// different furni, not the same one.
    /// </summary>
    [Fact]
    public void AStringListSurvivesInOrder()
    {
        IStuffData written = Factory.CreateStuffData(StuffDataType.StringKey);
        List<string> expected = [.. ((IStringStuffData)written).Data, "first", "second", "third"];

        ((IStringStuffData)written).Data.AddRange(["first", "second", "third"]);

        IStringStuffData read = (IStringStuffData)
            Factory.CreateStuffDataFromJson(StuffDataType.StringKey, Persist(written));

        read.Data.Should().Equal(expected);
    }

    [Fact]
    public void ANumberListSurvivesInOrder()
    {
        IStuffData written = Factory.CreateStuffData(StuffDataType.NumberKey);
        List<int> expected = [.. ((INumberStuffData)written).Data, 7, 0, 42];

        ((INumberStuffData)written).Data.AddRange([7, 0, 42]);

        INumberStuffData read = (INumberStuffData)
            Factory.CreateStuffDataFromJson(StuffDataType.NumberKey, Persist(written));

        read.Data.Should().Equal(expected);
    }

    [Fact]
    public void ACrackablesProgressSurvives()
    {
        IStuffData written = Factory.CreateStuffData(StuffDataType.CrackableKey);
        ((ICrackableStuffData)written).SetTarget(20);
        ((ICrackableStuffData)written).AddHit();
        ((ICrackableStuffData)written).AddHit();

        ICrackableStuffData read = (ICrackableStuffData)
            Factory.CreateStuffDataFromJson(StuffDataType.CrackableKey, Persist(written));

        // Losing the hit count re-arms an egg somebody has already been hitting for a week.
        read.Hits.Should().Be(2);
        read.Target.Should().Be(20);
    }

    [Fact]
    public void AHighscoreBoardSurvives()
    {
        IStuffData written = Factory.CreateStuffData(StuffDataType.HighscoreKey);
        IHighscoreStuffData board = (IHighscoreStuffData)written;
        board.SetScoreType(1);
        board.SetClearType(2);
        board.Entries.Add(
            new HighscoreEntry
            {
                Score = 31,
                Win = true,
                RecordedUtcTicks = 638_000_000_000_000_000,
                Names = ["alice", "bob"],
            }
        );

        IHighscoreStuffData read = (IHighscoreStuffData)
            Factory.CreateStuffDataFromJson(StuffDataType.HighscoreKey, Persist(written));

        read.ScoreType.Should().Be(1);
        read.ClearType.Should().Be(2);
        read.Entries.Should().ContainSingle();
        read.Entries[0].Score.Should().Be(31);
        read.Entries[0].Win.Should().BeTrue();
        // The timestamp is what lets a weekly board prune its own window; losing it un-prunes the board.
        read.Entries[0].RecordedUtcTicks.Should().Be(638_000_000_000_000_000);
        read.Entries[0].Names.Should().Equal(["alice", "bob"]);
    }

    [Fact]
    public void AUniqueSerialSurvives()
    {
        IStuffData written = Factory.CreateStuffData(StuffDataType.LegacyKey);
        IStuffData read = Factory.CreateStuffDataFromJson(
            StuffDataType.LegacyKey,
            Section("""{"UniqueNumber":7,"UniqueSeries":100,"Data":"1"}""")
        );

        read.UniqueNumber.Should().Be(7);
        read.UniqueSeries.Should().Be(100);
        read.IsUnique().Should().BeTrue();
        written.IsUnique().Should().BeFalse("a plain item carries no serial");
    }

    /// <summary>
    /// A stored section is read whichever casing wrote it.
    /// </summary>
    /// <remarks>
    /// Not a hypothetical. The persist path serializes the stuff data with the default (Pascal)
    /// options and hands the resulting node to a writer whose own options are camelCase — which does
    /// not rename an already-materialised node, while a caller passing a plain object to that same
    /// writer does get camelCase. So both spellings genuinely exist in the furniture table, and only
    /// the reader being case-insensitive keeps them all readable.
    /// </remarks>
    [Theory]
    [InlineData("""{"Data":"6"}""")]
    [InlineData("""{"data":"6"}""")]
    public void EitherCasingReadsBack(string stuff)
    {
        IStuffData read = Factory.CreateStuffDataFromJson(StuffDataType.LegacyKey, Section(stuff));

        read.GetLegacyString().Should().Be("6");
    }

    /// <summary>
    /// Read, written back, read again — twice through is the same as once. A room that activates,
    /// saves and deactivates repeatedly does exactly this, and a lossy step compounds silently.
    /// </summary>
    [Fact]
    public void TwoRoundTripsAreTheSameAsOne()
    {
        IStuffData first = Factory.CreateStuffDataFromJson(
            StuffDataType.MapKey,
            Section("""{"Data":{"state":"2","colour":"red"}}""")
        );

        IStuffData second = Factory.CreateStuffDataFromJson(StuffDataType.MapKey, Persist(first));
        IStuffData third = Factory.CreateStuffDataFromJson(StuffDataType.MapKey, Persist(second));

        ((IMapStuffData)third).Data.Should().Contain("state", "2").And.Contain("colour", "red");
    }

    /// <summary>
    /// Junk in the column is defaulted, not thrown.
    /// </summary>
    /// <remarks>
    /// This one found something. <c>extra_data</c> is a free string written by imports, admin edits
    /// and one-off SQL as much as by us, and both <c>"not json"</c> and <c>{"stuff":null}</c> threw —
    /// inside a room's activation, so one malformed row stopped a whole room from opening. An item
    /// with no readable state is a defaulted item; a room nobody can enter is worse.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"stuff":null}""")]
    [InlineData("""{"other":{"x":1}}""")]
    public void AnUnreadableSectionDefaultsRatherThanThrowing(string extraData)
    {
        IStuffData read = Factory.CreateStuffDataFromJson(StuffDataType.LegacyKey, extraData);

        read.StuffType.Should().Be(StuffDataType.LegacyKey);
    }

    /// <summary>Exactly what <c>FurnitureLogic.PersistStuffDataAsync</c> writes into the item's row.</summary>
    private static string Persist(IStuffData stuffData)
    {
        JsonObject root = new()
        {
            [ExtraDataSectionType.STUFF] = JsonSerializer.SerializeToNode(
                stuffData,
                stuffData.GetType()
            ),
        };

        return root.ToJsonString();
    }

    private static string Section(string stuff) => $$"""{"stuff":{{stuff}}}""";
}
