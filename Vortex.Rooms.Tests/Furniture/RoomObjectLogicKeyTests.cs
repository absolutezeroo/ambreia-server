using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Rooms.Providers;
using Vortex.Runtime.AssemblyProcessing;
using Vortex.Tests.Support;
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

    /// <summary>
    /// Rollers were removed (M0 bis), but the catalogue still carries definitions whose logic is
    /// <c>roller</c>. With nothing claiming the name they must load as the plain floor logic — a
    /// definition that failed to load would take its room down with it.
    /// </summary>
    [Fact]
    public void ARollerDefinition_LoadsAsDefaultLogic() =>
        ResolvedImplementation("roller").Should().Be<FurnitureFloorLogic>();

    /// <summary>
    /// Pet toys, the trampoline and the nest were removed with the pets' life sim (M0 bis); their
    /// definitions load as plain floor furniture, for the same reason as the roller above.
    /// </summary>
    [Theory]
    [InlineData("pet_toy")]
    [InlineData("pet_trampoline")]
    [InlineData("nest")]
    [InlineData("pet_nest")]
    public void APetToyOrNestDefinition_LoadsAsDefaultLogic(string key) =>
        ResolvedImplementation(key).Should().Be<FurnitureFloorLogic>();

    private static Type ResolvedImplementation(string key)
    {
        RoomObjectLogicProvider provider = new(
            FakeProxy.Create<IServiceProvider>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            NullLogger<RoomObjectLogicProvider>.Instance
        );

        // What RoomObjectLogicFeatureProcessor registers at startup, with a factory that only
        // records which class the name resolved to.
        foreach (
            Type logic in AssemblyExplorer.FindAssignees(
                typeof(FurnitureFloorLogic).Assembly,
                typeof(IRoomObjectLogic)
            )
        )
        {
            foreach (
                RoomObjectLogicAttribute attribute in logic.GetCustomAttributes<RoomObjectLogicAttribute>(
                    false
                )
            )
            {
                provider.RegisterLogic(
                    attribute.Key,
                    logic,
                    FakeProxy.Create<IServiceProvider>(_ => null),
                    (_, _) => new ResolvedLogic(logic)
                );
            }
        }

        return provider
            .CreateLogicInstance(key, FakeProxy.Create<IRoomFloorItemContext>(_ => null))
            .Should()
            .BeOfType<ResolvedLogic>()
            .Which.Implementation;
    }

    private sealed class ResolvedLogic(Type implementation) : IRoomObjectLogic
    {
        public Type Implementation { get; } = implementation;

        public IRoomObjectContext Context { get; set; } = null!;

        public Task OnAttachAsync(CancellationToken ct) => Task.CompletedTask;

        public Task OnDetachAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
