using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Spec F3b §4: a zone's decor is frozen. Without an interaction of its own a click does nothing —
/// no Habbo toggle, whatever the clicker's rights; its Interact triggers still fire.
/// </summary>
public sealed class ZoneFrozenDecorTests
{
    private static readonly RoomObjectId Decor = new(1);

    /// <summary>Every furniture use the Habbo path made on the decor.</summary>
    private readonly List<string> _uses = [];

    /// <summary>A two-state furni anyone may toggle, as a lamp is: the Habbo path would use it.</summary>
    private IFurnitureFloorLogic ToggleLogic() =>
        FakeProxy.Create<IFurnitureFloorLogic>(call =>
        {
            if (call.Method.Name == nameof(IFurnitureFloorLogic.OnUseAsync))
            {
                _uses.Add("use");
            }

            return call.Method.Name switch
            {
                nameof(IFurnitureFloorLogic.GetUsagePolicy) => FurnitureUsageType.Everybody,
                nameof(IFurnitureFloorLogic.CanWalk) => false,
                nameof(IFurnitureFloorLogic.CanStack) => false,
                nameof(IFurnitureFloorLogic.CanSit) => false,
                nameof(IFurnitureFloorLogic.CanLay) => false,
                nameof(IFurnitureFloorLogic.GetPostureOffset) => Altitude.Zero,
                _ => null,
            };
        });

    private static ZoneDecorObject Lamp(string? interaction = null) =>
        new()
        {
            Id = "lamp",
            Definition = ZoneSamples.Throne,
            Class = "throne",
            X = 3,
            Y = 2,
            Interaction = interaction,
        };

    private async Task<RoomHarness> ZoneWithAsync(ZoneDocument doc)
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);

        harness.LogicFactory = ToggleLogic;
        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);

        await EnterAsync(harness);

        return harness;
    }

    private Task<RoomHarness> ZoneWithAsync(ZoneDecorObject decor) =>
        ZoneWithAsync(ZoneSamples.Minimal() with { Decor = [decor] });

    private static Task<bool> UseAsync(RoomHarness harness, Primitives.Players.PlayerId by) =>
        harness.Grain.UseItemByIdAsync(harness.ContextFor(by), Decor, default);

    [Fact]
    public async Task TheOwner_CannotToggleFrozenDecor()
    {
        RoomHarness harness = await ZoneWithAsync(Lamp());

        (await UseAsync(harness, RoomHarness.Owner)).Should().BeFalse();

        _uses.Should().BeEmpty();
    }

    [Fact]
    public async Task AStranger_CannotToggleFrozenDecorEither()
    {
        RoomHarness harness = await ZoneWithAsync(Lamp());

        (await UseAsync(harness, RoomHarness.Stranger)).Should().BeFalse();

        _uses.Should().BeEmpty();
    }

    [Fact]
    public async Task ALever_StillFlips()
    {
        RoomHarness harness = await ZoneWithAsync(Lamp("lever"));

        (await UseAsync(harness, Walker)).Should().BeTrue();
    }

    [Fact]
    public async Task AnInteractTrigger_StillFiresOnFrozenDecor()
    {
        ZoneTrigger speaks = new()
        {
            Id = "speaks",
            Type = ZoneTriggerType.Interact,
            Target = "lamp",
            Actions = [new ShowDialogueAction("La lampe grésille")],
        };
        RoomHarness harness = await ZoneWithAsync(
            ZoneSamples.Minimal() with
            {
                Decor = [Lamp()],
                Triggers = [speaks],
            }
        );

        await UseAsync(harness, Walker);

        harness
            .ComposersSent.Select(c => c.Composer)
            .OfType<NotificationDialogMessageComposer>()
            .Should()
            .NotBeEmpty();
        _uses.Should().BeEmpty();
    }

    /// <summary>Not a zone (no runtime): the Habbo path stays — houses will need it.</summary>
    [Fact]
    public async Task OutsideAZone_TheHabboPathStays()
    {
        RoomHarness harness = await ZoneWithAsync(Lamp());
        harness.Grain._state.ZoneRuntime = null;

        await UseAsync(harness, RoomHarness.Owner);

        _uses.Should().Equal("use");
    }
}
