using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Rooms.Object.Logic.Furniture.Wall;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// The catalogue binds furniture to logic by the name the client's assets declare. Two of those
/// names are shared by floor and wall furniture, which the registry has to keep apart.
/// </summary>
public sealed class RoomObjectLogicFamilyTests
{
    private static string[] KeysOf<T>() =>
        [.. typeof(T).GetCustomAttributes<RoomObjectLogicAttribute>(false).Select(a => a.Key)];

    /// <summary>
    /// <c>furniture_multistate</c> and <c>furniture_basic</c> cover ~40 000 floor definitions and 824
    /// wall ones. Registering either name for one family only used to mean the other family resolved
    /// to it — and a wall item handed a floor logic cannot be constructed at all, because
    /// <c>IRoomWallItemContext</c> and <c>IRoomFloorItemContext</c> share no interface. That is why
    /// the provider keys registrations by (name, family); this test is what stops the two lists
    /// drifting apart.
    /// </summary>
    [Theory]
    [InlineData("furniture_basic")]
    [InlineData("furniture_multistate")]
    [InlineData("furniture_muItistate")]
    [InlineData("furniture_static")]
    public void ThePlainLogicNamesAreRegisteredForBothFamilies(string logicName)
    {
        KeysOf<FurnitureFloorLogic>().Should().Contain(logicName);
        KeysOf<FurnitureWallLogic>().Should().Contain(logicName);
    }

    [Fact]
    public void EachFamilyKeepsItsOwnDefault()
    {
        KeysOf<FurnitureFloorLogic>().Should().Contain("default_floor");
        KeysOf<FurnitureWallLogic>().Should().Contain("default_wall");
    }

    /// <summary>
    /// The Arcturus names are what the shipped catalogue carries; the <c>furniture_*</c> ones are what
    /// the assets and the client call the same furni. Dropping either side silently un-binds several
    /// hundred definitions, depending on which dump a hotel imported.
    /// </summary>
    [Theory]
    [InlineData(typeof(FurnitureDiceLogic), "dice", "furniture_dice")]
    [InlineData(typeof(FurnitureFireworksLogic), "fireworks", "furniture_fireworks")]
    public void LogicsCarryBothTheArcturusAndTheClientName(
        Type logic,
        string arcturusName,
        string clientName
    )
    {
        string[] keys =
        [
            .. logic.GetCustomAttributes<RoomObjectLogicAttribute>(false).Select(a => a.Key),
        ];

        keys.Should().Contain(arcturusName);
        keys.Should().Contain(clientName);
    }

    /// <summary>
    /// The pet furni the room AI looks for. A pet only finds a nest if the definition resolved to
    /// the matching logic in the first place, and the shipped catalogue spells it the Arcturus way
    /// -- <c>nest</c>, not the <c>pet_nest</c> Vortex invented. Every nest in the hotel used to fall
    /// back to <c>default_floor</c>, so no pet ever slept on one.
    /// </summary>
    /// <remarks>
    /// The assets are no help here: they call a nest <c>furniture_multistate</c>, which is the same
    /// trap <see cref="TheGateIsNotAPlainMultiStateFurni" /> guards against.
    /// </remarks>
    [Theory]
    [InlineData(typeof(FurniturePetNestLogic), "nest")]
    [InlineData(typeof(FurniturePetNestLogic), "pet_nest")]
    public void ThePetFurniCarryEverySpellingTheCatalogueUses(Type logic, string logicName)
    {
        string[] keys =
        [
            .. logic.GetCustomAttributes<RoomObjectLogicAttribute>(false).Select(a => a.Key),
        ];

        keys.Should().Contain(logicName);
    }

    [Fact]
    public void TheNestIsNotAPlainMultiStateFurni()
    {
        KeysOf<FurniturePetNestLogic>().Should().NotContain("furniture_multistate");
    }

    /// <summary>
    /// A gate is the standing counter-example to "just take the logic from the assets": the client
    /// calls it <c>furniture_multistate</c> because Flash derives blocking from the visualization,
    /// while Vortex resolves walkability server-side. 375 definitions were one blind remap away from
    /// becoming permanently walkable.
    /// </summary>
    [Fact]
    public void TheGateIsNotAPlainMultiStateFurni()
    {
        string[] keys = KeysOf<FurnitureGateLogic>();

        keys.Should().Contain("gate");
        keys.Should().NotContain("furniture_multistate");

        typeof(FurnitureGateLogic).Should().BeAssignableTo<FurnitureFloorLogic>();
    }
}
