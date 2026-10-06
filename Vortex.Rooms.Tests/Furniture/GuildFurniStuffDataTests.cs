using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Vortex.Furniture;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Groups;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Guild furniture rendered white in-game for as long as it has existed. Nothing threw: the furni
/// definitions shipped with <c>stuff_data_type = 0</c> (LegacyKey), the grant path only stamps the
/// guild identity onto <see cref="StuffDataType.StringKey"/> furni, so the stamping was skipped and
/// the client fell back to its own default colours. These tests pin the three things that had to
/// agree for the repair to work — the array layout, the on-disk shape, and the logic bindings.
/// </summary>
public sealed class GuildFurniStuffDataTests
{
    private static readonly StuffDataFactory Factory = new();

    private const string BadgeCode = "b18014b16014b06014b12014b06014";

    private static string[] KeysOf<T>() =>
        [.. typeof(T).GetCustomAttributes<RoomObjectLogicAttribute>(false).Select(a => a.Key)];

    [Fact]
    public void TheArrayMatchesTheIndicesTheClientReads()
    {
        // AS3 FurnitureGuildCustomizedLogic declares GUILD_ID=1, BADGE_CODE=2, COLOR_1=3, COLOR_2=4
        // and indexes all of them unconditionally, so a short or reordered array leaves the badge
        // and both recolours unset — which renders as the client's defaults, not as an error.
        List<string> data = GuildFurniStuffData.Build(7, BadgeCode, "63a600", "77c700");

        data.Should().HaveCount(5);
        data[GuildFurniStuffData.StateIndex].Should().Be("0");
        data[GuildFurniStuffData.GuildIdIndex].Should().Be("7");
        data[GuildFurniStuffData.BadgeCodeIndex].Should().Be(BadgeCode);
        data[GuildFurniStuffData.ColorOneIndex].Should().Be("63a600");
        data[GuildFurniStuffData.ColorTwoIndex].Should().Be("77c700");
    }

    [Fact]
    public void AnUnresolvableColourFallsBackToTheClientDefaults_NotToWhite()
    {
        List<string> data = GuildFurniStuffData.Build(7, BadgeCode, "", "   ");

        data[GuildFurniStuffData.ColorOneIndex].Should().Be(GuildFurniStuffData.DefaultColorOne);
        data[GuildFurniStuffData.ColorTwoIndex].Should().Be(GuildFurniStuffData.DefaultColorTwo);
    }

    /// <summary>
    /// Locks the exact blob a raw-SQL backfill writes (the former
    /// <c>SeedGuildFurniLogicAndStuffData</c> migration did, before M0 squashed it) against the
    /// reader that has to parse it. SQL cannot call <c>InventoryGrain.BuildGuildExtraData</c>, so
    /// the shape is duplicated by hand — a literal is the only thing that catches the two drifting
    /// apart.
    /// </summary>
    [Fact]
    public void TheBackfilledBlobDeserializesIntoTheGuildLayout()
    {
        const string Backfilled =
            """{"stuff":{"data":["0","1","b18014b16014b06014b12014b06014","63a600","77c700"]}}""";

        IStringStuffData read = (IStringStuffData)
            Factory.CreateStuffDataFromJson(StuffDataType.StringKey, Backfilled);

        read.Data[GuildFurniStuffData.GuildIdIndex].Should().Be("1");
        read.Data[GuildFurniStuffData.BadgeCodeIndex].Should().Be(BadgeCode);
        read.Data[GuildFurniStuffData.ColorOneIndex].Should().Be("63a600");
        read.Data[GuildFurniStuffData.ColorTwoIndex].Should().Be("77c700");
    }

    [Fact]
    public void TheGuildLayoutSurvivesAWriteThenRead()
    {
        ExtraData extraData = new(null);

        extraData.UpdateSection(
            ExtraDataSectionType.STUFF,
            new { Data = GuildFurniStuffData.Build(1, BadgeCode, "63a600", "77c700") }
        );

        IStringStuffData read = (IStringStuffData)
            Factory.CreateStuffDataFromJson(StuffDataType.StringKey, extraData.GetJsonString());

        read.Data[GuildFurniStuffData.ColorOneIndex].Should().Be("63a600");
        read.Data[GuildFurniStuffData.ColorTwoIndex].Should().Be("77c700");
    }

    [Fact]
    public void TheGuildLogicNamesMatchTheClient()
    {
        // Both strings are cases in the client's RoomObjectFactory. The definitions used to carry
        // the Arcturus interaction_type ('guild_furni', 'guild_gate') instead, which resolves to
        // nothing and falls back to default_floor.
        KeysOf<FurnitureGuildCustomizedLogic>().Should().Equal("furniture_guild_customized");
        KeysOf<FurnitureGroupForumTerminalLogic>().Should().Equal("furniture_group_forum_terminal");
    }

    /// <summary>
    /// The shared guild logic must not be a gate. It derived from
    /// <see cref="FurnitureGateLogic"/> while nothing in the database referenced it, so the
    /// mismatch was invisible — but pointing the definitions at it would have made
    /// <c>gld_carpet</c> and <c>gld_tile1</c>/<c>gld_tile2</c> (walkable, 11 and 4 states)
    /// impassable outside a single state, and restricted their use to room controllers. The client
    /// agrees: its FurnitureGuildCustomizedLogic extends FurnitureMultiStateLogic.
    /// </summary>
    [Fact]
    public void OnlyTheGateIsAGate()
    {
        typeof(FurnitureGuildCustomizedLogic).Should().NotBeAssignableTo<FurnitureGateLogic>();
        typeof(FurnitureGroupForumTerminalLogic).Should().NotBeAssignableTo<FurnitureGateLogic>();

        typeof(FurnitureGuildGateLogic).Should().BeAssignableTo<FurnitureGateLogic>();
        KeysOf<FurnitureGuildGateLogic>().Should().Equal("furniture_guild_gate");
    }
}
