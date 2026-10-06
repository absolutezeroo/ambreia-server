using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// A logic key is a wire contract, not an internal name: the server sends it to the client, which
/// resolves it against its own RoomObjectLogicEnum and falls back to the default logic on anything
/// it does not recognise. That failure is silent — the furniture simply never animates or offers its
/// dialog — so the keys are pinned here rather than left to be re-typed from memory.
/// </summary>
public sealed class RoomObjectLogicKeyTests
{
    private static string[] KeysOf<T>() =>
        [.. typeof(T).GetCustomAttributes<RoomObjectLogicAttribute>(false).Select(a => a.Key)];

    [Fact]
    public void TheWheelUsesTheClientsLogicName_NotTheFurnitureClassname()
    {
        // "wheel_of_fortune" is the classname in furnidata; the client's enum knows
        // "furniture_habbowheel". Registering the classname matched server-side while the client
        // animated nothing.
        KeysOf<FurnitureWheelOfFortuneLogic>().Should().Equal("furniture_habbowheel");
    }

    [Fact]
    public void TheLogicAttributeIsRepeatable_SoOneClassCanCoverSeveralClientNames()
    {
        AttributeUsageAttribute usage =
            typeof(RoomObjectLogicAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        usage.AllowMultiple.Should().BeTrue();
    }

    [Fact]
    public void ThePurchasableClothingIsPlainFloorFurniture_ItsRedemptionIsAPacket()
    {
        // The client's own logic for this name extends FurnitureMultiStateLogic and overrides
        // useObject() to open a dialog, so nothing is ever "used" server-side — the redemption comes
        // in as RedeemPurchasableClothing. Binding the name to anything but the plain floor logic
        // would be inventing behaviour the client never asks for.
        KeysOf<FurnitureFloorLogic>().Should().Contain("furniture_purchasable_clothing");
    }
}
